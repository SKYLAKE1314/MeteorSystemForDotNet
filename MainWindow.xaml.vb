Imports System.Windows
Imports System.Windows.Controls

Class MainWindow

    Private PageCache As New Dictionary(Of String, Page)
    Private _windowdesigner As WindowDesigner
    Private _trayIcon As System.Windows.Forms.NotifyIcon

    Public Sub New()

        InitializeComponent()

        AddHandler LanguageManager.LanguageChanged, AddressOf RefreshLanguageUI

        _windowdesigner = New WindowDesigner(Me)
        _windowdesigner.EnableDrag(TitleBar)
        _windowdesigner.SetButtonActions(MinButton, MaxButton, CloseButton)

        InitTrayIcon()

        AddHandler Me.Loaded, AddressOf MainWindow_Loaded
        AddHandler Me.StateChanged, AddressOf MainWindow_StateChanged

    End Sub

    ' =========================
    ' 托盤圖示初始化
    ' =========================
    Private Sub InitTrayIcon()
        _trayIcon = New System.Windows.Forms.NotifyIcon()
        _trayIcon.Text = "MeteorSystem"

        ' 使用專案內嵌的 ICO 圖示（與應用程式圖示共用同一資源）
        Try
            Dim icoPath = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "54nkr-kmnbe-001.ico")
            If IO.File.Exists(icoPath) Then
                _trayIcon.Icon = New System.Drawing.Icon(icoPath)
            Else
                _trayIcon.Icon = System.Drawing.SystemIcons.Application
            End If
        Catch
            _trayIcon.Icon = System.Drawing.SystemIcons.Application
        End Try

        Dim menu As New System.Windows.Forms.ContextMenuStrip()
        Dim openItem As New System.Windows.Forms.ToolStripMenuItem("開啟視窗")
        Dim exitItem As New System.Windows.Forms.ToolStripMenuItem("退出")
        AddHandler openItem.Click, Sub(s, e) RestoreFromTray()
        AddHandler exitItem.Click, Sub(s, e) Application.Current.Shutdown()
        menu.Items.Add(openItem)
        menu.Items.Add(exitItem)
        _trayIcon.ContextMenuStrip = menu
        AddHandler _trayIcon.DoubleClick, Sub(s, e) RestoreFromTray()
    End Sub

    Private Sub MainWindow_StateChanged(sender As Object, e As EventArgs)
        If WindowState = WindowState.Minimized AndAlso My.Settings.SilentStart Then
            Me.ShowInTaskbar = False
            _trayIcon.Visible = True
        End If
    End Sub

    Private Sub RestoreFromTray()
        Me.Show()
        Me.WindowState = WindowState.Normal
        Me.ShowInTaskbar = True
        _trayIcon.Visible = False
    End Sub

    Private Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs)

        PreloadPages()

        If NavList.Items.Count > 0 Then
            NavList.SelectedIndex = 0
        End If

        RefreshLanguageUI()

        ' 靜默啟動：自動最小化至托盤
        If My.Settings.SilentStart Then
            Me.WindowState = WindowState.Minimized
            Me.ShowInTaskbar = False
            _trayIcon.Visible = True
        End If

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
