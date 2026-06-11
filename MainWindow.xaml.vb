Imports System.Windows
Imports System.Windows.Controls

Class MainWindow

    Private PageCache As New Dictionary(Of String, Page)

    Private _windowdesigner As WindowDesigner

    Public Sub New()

        InitializeComponent()

        AddHandler LanguageManager.LanguageChanged, AddressOf RefreshLanguageUI

        _windowdesigner = New WindowDesigner(Me)
        _windowdesigner.EnableDrag(TitleBar)
        _windowdesigner.SetButtonActions(MinButton, MaxButton, CloseButton)

        AddHandler Me.Loaded, AddressOf MainWindow_Loaded

    End Sub

    Private Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs)

        PreloadPages()

        NavList.SelectedIndex = 0

        'Try
        '    Dim kawaiiWindow As New KawaiiJK()
        '    kawaiiWindow.Show()
        'Catch ex As Exception
        '    MessageBox.Show($"開啟live2d失敗: {ex.Message}")
        'End Try

        ' ⭐ 加這個
        RefreshLanguageUI()

    End Sub

    ' =========================
    ' 預載頁面
    ' =========================
    Private Sub PreloadPages()

        Dim pageMap As New Dictionary(Of String, Type) From {
            {"HomePage", GetType(HomePage)},
            {"DetectionPage", GetType(DetectionPage)},
            {"AlgorithmPage", GetType(AlgorithmPage)},
            {"ProcessPage", GetType(ProcessPage)},
            {"SettingPage", GetType(SettingPage)}
        }

        For Each kv In pageMap

            Dim pageInstance As Page =
                CType(Activator.CreateInstance(kv.Value), Page)

            PageCache(kv.Key) = pageInstance

        Next

    End Sub

    ' =========================
    ' 導航
    ' =========================
    Private Sub NavigateTo(pageName As String)

        If PageCache.ContainsKey(pageName) Then
            ContentFrame.Navigate(PageCache(pageName))
        End If

    End Sub

    Private Sub NavList_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        Dim item = TryCast(NavList.SelectedItem, ListBoxItem)
        If item Is Nothing Then Return

        NavigateTo(item.Tag.ToString())

    End Sub

    ' =========================
    ' 折疊（不再寫死文字）
    ' =========================
    Private Sub CollapseBtn_Checked(sender As Object, e As RoutedEventArgs)

        NavColumn.Width = New GridLength(60)
        SideTitle.Visibility = Visibility.Collapsed

        For Each item As ListBoxItem In NavList.Items
            item.Content = ""
        Next

    End Sub

    Private Sub CollapseBtn_Unchecked(sender As Object, e As RoutedEventArgs)

        NavColumn.Width = New GridLength(240)
        SideTitle.Visibility = Visibility.Visible

        RefreshLanguageUI()

    End Sub

    ' =========================
    ' ⭐ 全局語言刷新（核心）
    ' =========================
    Public Sub RefreshLanguageUI()

        Dim items = NavList.Items

        If items.Count < 4 Then Return

        CType(items(0), ListBoxItem).Content = LanguageManager.T("Nav_Run")
        CType(items(1), ListBoxItem).Content = LanguageManager.T("Nav_AI")
        CType(items(2), ListBoxItem).Content = LanguageManager.T("Nav_Algorithm")
        CType(items(3), ListBoxItem).Content = LanguageManager.T("Nav_Process")
        CType(items(4), ListBoxItem).Content = LanguageManager.T("Nav_Setting")

        SideTitle.Text = LanguageManager.T("Side_Title")

    End Sub

End Class