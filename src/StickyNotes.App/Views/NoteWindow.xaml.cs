using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StickyNotes.App.Interop;
using StickyNotes.App.ViewModels;
using StickyNotes.App.Views.RichText;
using StickyNotes.Core.Abstractions;

namespace StickyNotes.App.Views;

/// <summary>Ventana de una nota.
/// Code-behind: sólo mecánica de vista. La excepción MVVM documentada es la sincronización del
/// RichTextBox: su Document no es enlazable (no es DependencyProperty), así que aquí se carga desde
/// <see cref="NoteViewModel.RichContent"/> y se serializa de vuelta con debounce.
/// Además: arrastre, estilos Win32 (fuera de Alt+Tab, protección de captura), mantener "topmost" y foco del checklist.</summary>
public partial class NoteWindow : Window
{
    private static readonly TimeSpan ContentDebounce = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan PositionDebounce = TimeSpan.FromMilliseconds(500);

    private readonly IScreenPrivacyService _privacy;
    private readonly Func<bool> _shouldProtect;
    private readonly DispatcherTimer _contentTimer;
    private readonly DispatcherTimer _positionTimer;
    private bool _isLoadingDocument;
    private bool _trackPosition;

    public NoteWindow(NoteViewModel viewModel, IScreenPrivacyService privacy, Func<bool> shouldProtect)
    {
        ViewModel = viewModel;
        _privacy = privacy;
        _shouldProtect = shouldProtect;
        DataContext = viewModel;

        // Los timers deben existir ANTES de InitializeComponent: al asignar el Document del RichTextBox
        // desde XAML se dispara TextChanged. Se ignora ese evento inicial con _isLoadingDocument.
        _contentTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ContentDebounce };
        _contentTimer.Tick += (_, _) => { _contentTimer.Stop(); CommitContent(); };
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PositionDebounce };
        _positionTimer.Tick += (_, _) => { _positionTimer.Stop(); CommitPosition(); };

        _isLoadingDocument = true;
        InitializeComponent();
        _isLoadingDocument = false;

        // Note.Left/Top están en píxeles físicos: la posición real se aplica con SetWindowPos en
        // SourceInitialized (antes de mostrarse). Aquí sólo un valor inicial aproximado.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = viewModel.Note.Left;
        Top = viewModel.Note.Top;

        // El tamaño NO se enlaza en XAML: Windows redimensiona la ventana (WindowChrome, WM_DPICHANGED) y
        // WPF sustituye el binding de Window.Width/Height por un valor local, con lo que dejaría de actualizarse.
        ApplySize();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        LoadDocument();
        Body.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnHyperlinkRequestNavigate));
        DataObject.AddPastingHandler(Body, OnBodyPasting);
        Body.AddHandler(TextInputEvent, new TextCompositionEventHandler(OnBodyTextInput), handledEventsToo: true);

        SourceInitialized += OnSourceInitialized;
        // Sólo se registran movimientos tras mostrarse: los ajustes de DPI al crear la ventana no son del usuario.
        ContentRendered += (_, _) => _trackPosition = true;
        LocationChanged += (_, _) => { if (_trackPosition) RestartTimer(_positionTimer); };
        Deactivated += OnDeactivated;
        Closing += OnClosing;
        Closed += OnClosed;
        viewModel.ChecklistFocusRequested += OnChecklistFocusRequested;
    }

    public NoteViewModel ViewModel { get; }

    public Guid NoteId => ViewModel.Id;

    /// <summary>HWND (0 hasta SourceInitialized).</summary>
    public nint Handle { get; private set; }

    /// <summary>Sólo el administrador de ventanas puede cerrar la nota (Alt+F4 no la cierra).</summary>
    public bool AllowClose { get; set; }

    // ───────────── Win32 ─────────────

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Handle = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeToolWindow(Handle);
        NativeMethods.TryRoundCorners(Handle);
        ApplyPrivacy(_shouldProtect());
        // Posición guardada en píxeles físicos (correcta en monitores con escalas distintas).
        // Si el monitor destino tiene otro DPI, WPF (PerMonitorV2) reescala la ventana al recibir WM_DPICHANGED.
        NativeMethods.MoveTo(Handle, (int)Math.Round(ViewModel.Note.Left), (int)Math.Round(ViewModel.Note.Top));
    }

    /// <summary>Mueve la nota a una posición en píxeles físicos y la registra.</summary>
    public void MoveToPixels(double left, double top)
    {
        if (Handle != 0) NativeMethods.MoveTo(Handle, (int)Math.Round(left), (int)Math.Round(top));
        ViewModel.UpdatePosition(left, top);
    }

    public void ApplyPrivacy(bool protect)
    {
        if (Handle == 0) return;
        if (protect) _privacy.Protect(Handle);
        else _privacy.Unprotect(Handle);
    }

    /// <summary>Vuelve a la cima de la banda "topmost" sin robar el foco.</summary>
    public void ReassertTopmost()
    {
        if (Handle == 0 || !IsVisible) return;
        NativeMethods.BringToTopmost(Handle);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        ViewModel.IsReminderPopupOpen = false;
        // Tras ceder el foco, otra app "topmost" puede quedar encima: se reafirma salvo que el
        // foco haya ido a otra ventana nuestra (búsqueda, papelera, diálogos).
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!NativeMethods.IsForegroundOwnedByThisProcess() && !NativeMethods.ShouldYieldTopmost()) ReassertTopmost();
        });
    }

    // ───────────── Texto enriquecido ─────────────

    private void LoadDocument()
    {
        _isLoadingDocument = true;
        try
        {
            FlowDocumentSerializer.Load(Body.Document, ViewModel.RichContent, ViewModel.PlainText);
        }
        finally
        {
            _isLoadingDocument = false;
        }
    }

    private void Body_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingDocument) return;
        // Inmediato: ocultar el texto de ayuda en la primera pulsación (el guardado sí va con debounce).
        ViewModel.IsBodyEmpty = IsDocumentEmpty();
        RestartTimer(_contentTimer);
    }

    /// <summary>Vacío = sin texto y sin listas (una viñeta vacía ya no debe mostrar el texto de ayuda encima).</summary>
    private bool IsDocumentEmpty()
    {
        var blocks = Body.Document.Blocks;
        if (blocks.Count > 1 || blocks.FirstBlock is System.Windows.Documents.List) return false;
        return string.IsNullOrWhiteSpace(new System.Windows.Documents.TextRange(Body.Document.ContentStart, Body.Document.ContentEnd).Text);
    }

    private void CommitContent()
    {
        var (rich, plain) = FlowDocumentSerializer.Save(Body.Document);
        ViewModel.UpdateContent(rich, plain);
    }

    private void CommitPosition()
    {
        if (NativeMethods.TryGetPosition(Handle, out var x, out var y)) ViewModel.UpdatePosition(x, y);
    }

    /// <summary>Aplica de inmediato los cambios pendientes del debounce (antes de guardar/cerrar).</summary>
    public void CommitPendingEdits()
    {
        if (_contentTimer.IsEnabled) { _contentTimer.Stop(); CommitContent(); }
        if (_positionTimer.IsEnabled) { _positionTimer.Stop(); CommitPosition(); }
    }

    private static void RestartTimer(DispatcherTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    // ───────────── Vínculos ─────────────

    private Hyperlink? _editingLink;
    private TextRange? _linkTarget;

    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        NoteLinks.TryOpen(e.Uri);
    }

    private void Body_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OpenLinkPopup();
        }
        else if (e.Key == Key.Enter)
        {
            // Enter inserta el salto de párrafo durante el propio KeyDown: al ejecutarse esto ya está aplicado.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => NoteLinks.LinkifyWordBefore(Body.Document, Body.CaretPosition));
        }
    }

    /// <summary>Espacio/Tab llegan como TextInput (WM_CHAR), después del KeyDown. Con handledEventsToo este
    /// manejador corre tras la inserción del RichTextBox: "www.ejemplo.com␣" → vínculo.</summary>
    private void OnBodyTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length > 0 && char.IsWhiteSpace(e.Text[^1]))
            NoteLinks.LinkifyWordBefore(Body.Document, Body.CaretPosition);
    }

    private void OnBodyPasting(object sender, DataObjectPastingEventArgs e)
    {
        // Sólo se enlaza lo pegado: posición inicial con gravedad hacia atrás para que no avance con el texto insertado.
        var start = Body.Selection.Start.GetPositionAtOffset(0, LogicalDirection.Backward);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (start is not null) NoteLinks.Linkify(Body.Document, start, Body.CaretPosition);
        });
    }

    private void LinkButton_Click(object sender, RoutedEventArgs e) => OpenLinkPopup();

    private void OpenLinkPopup()
    {
        var selection = Body.Selection;
        _linkTarget = new TextRange(selection.Start, selection.End);
        _editingLink = NoteLinks.FindAt(selection.Start) ?? (selection.IsEmpty ? null : NoteLinks.FindAt(selection.End));

        var selectedText = selection.Text.Trim();
        LinkUrlBox.Text = _editingLink?.NavigateUri?.OriginalString
                          ?? (NoteLinks.TryNormalize(selectedText, out _) ? selectedText : string.Empty);
        LinkTextBox.Text = string.Empty;
        LinkTextPanel.Visibility = _editingLink is null && selection.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        LinkRemoveButton.Visibility = _editingLink is null ? Visibility.Collapsed : Visibility.Visible;
        LinkError.Visibility = Visibility.Collapsed;

        if (!IsActive) Activate();
        LinkPopup.IsOpen = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            LinkUrlBox.Focus();
            LinkUrlBox.SelectAll();
        });
    }

    private void LinkApply_Click(object sender, RoutedEventArgs e) => ApplyLink();

    private void LinkUrlBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; ApplyLink(); }
        else if (e.Key == Key.Escape) { e.Handled = true; CloseLinkPopup(); }
    }

    private void LinkCancel_Click(object sender, RoutedEventArgs e) => CloseLinkPopup();

    private void LinkRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_editingLink is { Parent: not null } link)
        {
            NoteLinks.Unwrap(link);
            RestartTimer(_contentTimer);
        }
        CloseLinkPopup();
    }

    private void ApplyLink()
    {
        if (!NoteLinks.TryNormalize(LinkUrlBox.Text, out var uri))
        {
            ShowLinkError("Escriba una dirección web (p. ej. www.ejemplo.com) o un correo.");
            return;
        }

        TextPointer? caretAfter = null;
        Body.BeginChange();
        try
        {
            if (_editingLink is { Parent: not null } existing)
            {
                existing.NavigateUri = uri;
            }
            else if (_linkTarget is { IsEmpty: false } target)
            {
                var link = new Hyperlink(target.Start, target.End) { NavigateUri = uri };
                caretAfter = link.ElementEnd;
            }
            else
            {
                var text = string.IsNullOrWhiteSpace(LinkTextBox.Text) ? LinkUrlBox.Text.Trim() : LinkTextBox.Text.Trim();
                var position = (_linkTarget?.Start ?? Body.CaretPosition).GetInsertionPosition(LogicalDirection.Forward);
                var link = new Hyperlink(new Run(text), position) { NavigateUri = uri };
                caretAfter = link.ElementEnd;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            ShowLinkError("Seleccione texto de un solo párrafo para convertirlo en vínculo.");
            return;
        }
        finally
        {
            Body.EndChange();
        }

        RestartTimer(_contentTimer); // cambiar NavigateUri no siempre dispara TextChanged
        CloseLinkPopup();
        if (caretAfter is not null) Body.CaretPosition = caretAfter;
    }

    private void ShowLinkError(string message)
    {
        LinkError.Text = message;
        LinkError.Visibility = Visibility.Visible;
    }

    private void CloseLinkPopup()
    {
        LinkPopup.IsOpen = false;
        Body.Focus();
    }

    private void LinkPopup_Closed(object? sender, EventArgs e)
    {
        _editingLink = null;
        _linkTarget = null;
    }

    // ───────────── Interacción ─────────────

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || ViewModel.IsPositionLocked) return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* el botón ya se soltó */ }
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is not { } menu) return;
        menu.DataContext = ViewModel;
        menu.PlacementTarget = MoreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void CancelReminder_Click(object sender, RoutedEventArgs e) => ViewModel.IsReminderPopupOpen = false;

    private void ChecklistText_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ChecklistItemViewModel item } box) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ViewModel.InsertChecklistItemAfter(item);
        }
        else if (e.Key == Key.Back && box.Text.Length == 0)
        {
            e.Handled = true;
            ViewModel.RemoveChecklistItem(item);
        }
    }

    private void OnChecklistFocusRequested(object? sender, ChecklistItemViewModel item)
    {
        // El contenedor del nuevo elemento se genera tras el layout.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ChecklistItems.ItemContainerGenerator.ContainerFromItem(item) is not DependencyObject container) return;
            var box = FindDescendant<TextBox>(container);
            if (box is null) return;
            if (!IsActive) Activate();
            box.Focus();
            box.CaretIndex = box.Text.Length;
        });
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>Trae la nota al frente, le da el foco y hace parpadear su marco.</summary>
    public void FocusAndFlash()
    {
        if (!IsVisible) Show();
        Activate();
        Body.Focus();
        Flash();
    }

    public void Flash()
    {
        var blink = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
        };
        FlashBorder.BeginAnimation(OpacityProperty, blink);
    }

    // ───────────── Cierre ─────────────

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            return;
        }
        CommitPendingEdits();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _contentTimer.Stop();
        _positionTimer.Stop();
        ViewModel.ChecklistFocusRequested -= OnChecklistFocusRequested;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    // ───────────── Tamaño ─────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NoteViewModel.Size) or nameof(NoteViewModel.Width) or nameof(NoteViewModel.Height))
            ApplySize();
    }

    /// <summary>Aplica el tamaño fijo de la nota (DIPs; WPF lo escala al DPI del monitor actual).</summary>
    private void ApplySize()
    {
        Width = ViewModel.Width;
        Height = ViewModel.Height;
    }
}
