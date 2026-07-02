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

        If NavList.Items.Count > 0 Then
            NavList.SelectedIndex = 0
        End If

        'Try
        '    Dim kawaiiWindow As New KawaiiJK()
        '    kawaiiWindow.Show()
        'Catch ex As Exception
        '    MessageBox.Show($"開啟live2d失敗: {ex.Message}")
        'End Try

        ' 相機訂閲
        'AddHandler Me.Loaded, Sub()
        '                          CameraService.Instance.StartAll()
        '                      End Sub
        ' ⭐ 加這個
        RefreshLanguageUI()

    End Sub

    ' =========================
    ' 預載頁面
    ' =========================
    Private Sub PreloadPages()
        If AppRuntime.Home Is Nothing Then
            AppRuntime.Home = New HomePage()
        End If

        If AppRuntime.Process Is Nothing Then
            AppRuntime.Process = New ProcessPage()
        End If

        PageCache("HomePage") = AppRuntime.Home
        PageCache("DetectionPage") = New DetectionPage()
        PageCache("AlgorithmPage") = New AlgorithmPage()
        PageCache("ModelEditPage") = New ModelEditPage()
        PageCache("ProcessPage") = AppRuntime.Process
        PageCache("SettingPage") = New SettingPage()
    End Sub
    ' =========================
    ' 導航
    ' =========================
    Private Sub NavigateTo(pageName As String)

        If String.IsNullOrWhiteSpace(pageName) Then Return

        If Not PageCache.ContainsKey(pageName) OrElse PageCache(pageName) Is Nothing Then
            Select Case pageName
                Case "HomePage"
                    If AppRuntime.Home Is Nothing Then AppRuntime.Home = New HomePage()
                    PageCache(pageName) = AppRuntime.Home
                Case "ProcessPage"
                    If AppRuntime.Process Is Nothing Then AppRuntime.Process = New ProcessPage()
                    PageCache(pageName) = AppRuntime.Process
                Case "DetectionPage"
                    PageCache(pageName) = New DetectionPage()
                Case "AlgorithmPage"
                    PageCache(pageName) = New AlgorithmPage()
                Case "ModelEditPage"
                    PageCache(pageName) = New ModelEditPage()
                Case "SettingPage"
                    PageCache(pageName) = New SettingPage()
                Case Else
                    Return
            End Select
        End If

        Dim target = PageCache(pageName)
        If ContentFrame.Content Is target Then Return

        ContentFrame.Navigate(target)

    End Sub

    Private Sub NavList_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        Dim item = TryCast(NavList.SelectedItem, ListBoxItem)
        If item Is Nothing Then Return

        Dim pageTag = TryCast(item.Tag, String)
        If String.IsNullOrWhiteSpace(pageTag) Then Return

        NavigateTo(pageTag)

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

        If items.Count < 6 Then Return

        CType(items(0), ListBoxItem).Content = LanguageManager.T("Nav_Run")
        CType(items(1), ListBoxItem).Content = LanguageManager.T("Nav_AI")
        CType(items(2), ListBoxItem).Content = LanguageManager.T("Nav_Algorithm")
        CType(items(3), ListBoxItem).Content = LanguageManager.T("Nav_ModelEdit")
        CType(items(4), ListBoxItem).Content = LanguageManager.T("Nav_Process")
        CType(items(5), ListBoxItem).Content = LanguageManager.T("Nav_Setting")

        SideTitle.Text = LanguageManager.T("Side_Title")
        MainTitleText.Text = LanguageManager.T("Main_Title")

    End Sub

End Class
