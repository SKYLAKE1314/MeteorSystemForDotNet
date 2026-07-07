Imports System.Windows
Imports System.Windows.Controls
Imports System.ComponentModel
Imports System.Collections.ObjectModel

Public Class SettingPage

    Private _isLoaded As Boolean = False
    Private _suppressEvents As Boolean = False  ' 防止 LoadCameraRows 期間觸發連鎖事件

    Private _ioMode As IoBoardMode = IoBoardMode.NONE
    Private _cameraList As New List(Of CameraInfo)

    Public Property CameraRows As New ObservableCollection(Of CameraRow)
    Public Property RecordingCameraList As New ObservableCollection(Of CameraInfo)

    Public Sub New()
        InitializeComponent()

        CameraRows = New ObservableCollection(Of CameraRow)

        Me.DataContext = Me

        _isLoaded = False
        _ioMode = IoBoardModeHelper.Parse(My.Settings.IoMode)
        AppRuntime.IoMode = _ioMode

        ' Camera rows deferred to Loaded (async) — avoids UI freeze on first navigation
        Select Case _ioMode
            Case IoBoardMode.IO
                IoBoardComboBox.SelectedIndex = 0
            Case IoBoardMode.PLC
                IoBoardComboBox.SelectedIndex = 1
            Case Else
                IoBoardComboBox.SelectedIndex = 2
        End Select

        AddHandler Me.Loaded, AddressOf SettingPage_Loaded
        AddHandler LanguageManager.LanguageChanged, AddressOf LanguageChanged_Handler
        AddHandler Me.Unloaded, AddressOf SettingPage_Unloaded
    End Sub

    ' =========================
    ' Page Loaded
    ' =========================
    Private Async Sub SettingPage_Loaded(sender As Object, e As RoutedEventArgs)
        LoadingOverlay.Visibility = Visibility.Visible
        LoadingText.Text = "載入設定中..."

        Await Task.Yield()

        Try
            ' 相機列表刷新在背景執行緓（WMI 查詢 + DSHOW probe 很慢，不能附方在 UI 線程）
            Await Task.Run(Sub() CameraManager.Refresh())

            LoadCameraRows()
            _isLoaded = True
            RefreshLanguageUI()

            Select Case LanguageManager.CurrentLanguage
                Case "zhTW"
                    LanguageComboBox.SelectedIndex = 0
                Case "zhCN"
                    LanguageComboBox.SelectedIndex = 1
                Case "enUS"
                    LanguageComboBox.SelectedIndex = 2
                Case "jaJP"
                    LanguageComboBox.SelectedIndex = 3
                Case Else
                    LanguageComboBox.SelectedIndex = 0
            End Select

            ' 訂閱相機變更事件，即時更新 ComboBox
            AddHandler CameraManager.CameraChanged, AddressOf OnCameraChangedRefresh
        Finally
            LoadingOverlay.Visibility = Visibility.Collapsed
        End Try
    End Sub

    Private Sub LanguageChanged_Handler(sender As Object, e As EventArgs)
        RefreshLanguageUI()
    End Sub

    Private Sub SettingPage_Unloaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler CameraManager.CameraChanged, AddressOf OnCameraChangedRefresh
        RemoveHandler LanguageManager.LanguageChanged, AddressOf LanguageChanged_Handler
        RemoveHandler Me.Unloaded, AddressOf SettingPage_Unloaded
    End Sub

    Private Sub OnCameraChangedRefresh()
        ' CameraChanged 在背景線程觸發；必須派送回 UI 線程再操作 ObservableCollection
        Dispatcher.InvokeAsync(Sub() LoadCameraRows())
    End Sub

    Private Sub RefreshLanguageUI()
        TxtLanguageTitle.Text = LanguageManager.T("Setting_Language")
        TxtIoBoardTitle.Text = LanguageManager.T("Setting_IoBoard")
        TxtCameraTitle.Text = LanguageManager.T("Setting_Camera")
        TxtRecordingCameraTitle.Text = "錄影相機"
        TxtAutoRunTitle.Text = LanguageManager.T("Setting_AutoRun")
        TxtGpuTitle.Text = LanguageManager.T("Setting_GpuBoost")
    End Sub

    ' =========================
    ' Language Switch
    ' =========================
    Private Sub LanguageComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        If DesignerProperties.GetIsInDesignMode(Me) Then Return
        If Not _isLoaded Then Return

        Select Case LanguageComboBox.SelectedIndex
            Case 0 : LanguageManager.Load("zhTW")
                Logger.Debug("正體中文")
            Case 1 : LanguageManager.Load("zhCN")
            Case 2 : LanguageManager.Load("enUS")
            Case 3 : LanguageManager.Load("jaJP")
        End Select

        Dim main = TryCast(Application.Current?.MainWindow, MainWindow)
        If main IsNot Nothing Then
            main.RefreshLanguageUI()
        End If


    End Sub
    ' 板卡

    Private Sub IoBoardComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        If Not _isLoaded Then Return
        If IoBoardComboBox.SelectedItem Is Nothing Then Return

        Select Case IoBoardComboBox.SelectedIndex
            Case 0
                _ioMode = IoBoardMode.IO
            Case 1
                _ioMode = IoBoardMode.PLC
            Case Else
                _ioMode = IoBoardMode.NONE
        End Select

        My.Settings.IoMode = _ioMode.ToString()
        My.Settings.Save()
        AppRuntime.IoMode = _ioMode

        ' 自動化
        AutoRun.SelectedIndex =
If(My.Settings.AutoRun, 0, 1)

    End Sub

    Private Sub Camera_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        If Not _isLoaded OrElse _suppressEvents Then Return

        Dim cb = DirectCast(sender, ComboBox)
        Dim row = DirectCast(cb.DataContext, CameraRow)

        Dim cam = TryCast(cb.SelectedItem, CameraInfo)
        row.SelectedCamera = cam

        RefreshAllCameraLists()
        SaveCameraRows()

        My.Settings.CameraDeviceId = cam?.DeviceId
        My.Settings.Save()

        ' 通知其他頁面相機設定已變更，不展開 SettingPage 自己的 reload
        CameraManager.NotifyCameraChanged()

    End Sub

    Private Async Sub Resolution_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        If Not _isLoaded OrElse _suppressEvents Then Return

        Dim cb = DirectCast(sender, ComboBox)
        Dim row = DirectCast(cb.DataContext, CameraRow)
        Dim res = TryCast(cb.SelectedItem, CameraResolutionOption)

        If res Is Nothing OrElse row.SelectedCamera Is Nothing Then Return

        row.SelectedResolution = res
        SaveResolutionRows()

        ' 重啟相機以套用新分辨率（在背景執行緒，避免 UI 凍結）
        Dim deviceId = row.SelectedCamera.DeviceId
        LoadingOverlay.Visibility = Visibility.Visible
        LoadingText.Text = $"套用分辨率 {res.Width}×{res.Height}..."
        Try
            Await Task.Run(Sub()
                               CameraService.Instance.StopCamera(deviceId)
                               CameraService.Instance.StartCamera(deviceId)
                           End Sub)
            Logger.Info($"[Setting] 相機 {row.Title} 分辨率更改為 {res.Width}x{res.Height}，已重啟相機")
        Finally
            LoadingOverlay.Visibility = Visibility.Collapsed
        End Try

    End Sub

    Private Sub LoadCameraRows()

        _suppressEvents = True
        Try
            Dim camList = CameraManager.GetCachedCameras()
            _cameraList = camList
            RecordingCameraList.Clear()
            For Each camera In camList
                RecordingCameraList.Add(camera)
            Next

            CameraRows.Clear()

            Dim savedIds = My.Settings.CameraDeviceIds
            Dim savedResolutions = My.Settings.CameraResolutions

            If savedIds IsNot Nothing AndAlso savedIds.Count > 0 Then

                For i = 0 To savedIds.Count - 1
                    Dim id = savedIds(i)

                    Dim camRow = New CameraRow With {
                    .Title = $"相機 {CameraRows.Count + 1}",
                    .CameraList = camList
                }

                    camRow.SelectedCamera =
                    camList.FirstOrDefault(Function(c) c.DeviceId = id)

                    ' 載入已儲存的分辨率
                    Dim resList = CameraResolutionOption.CommonResolutions()
                    camRow.ResolutionList = resList
                    If savedResolutions IsNot Nothing AndAlso savedResolutions.Count > i Then
                        Dim savedRes = CameraResolutionOption.FromTag(savedResolutions(i))
                        camRow.SelectedResolution =
                        resList.FirstOrDefault(Function(r) r.Equals(savedRes))
                    End If

                    CameraRows.Add(camRow)
                Next

            Else
                ' fallback
                CameraRows.Add(New CameraRow With {
                .Title = "相機 1",
                .CameraList = camList,
                .ResolutionList = CameraResolutionOption.CommonResolutions()
            })
            End If

            UpdateCameraButtons()
            RefreshAllCameraLists()

            Dim recordingId = My.Settings.RecordingCameraId
            If String.IsNullOrWhiteSpace(recordingId) AndAlso CameraRows.Count > 0 AndAlso CameraRows(0).SelectedCamera IsNot Nothing Then
                recordingId = CameraRows(0).SelectedCamera.DeviceId
            End If
            RecordingCameraComboBox.SelectedItem = RecordingCameraList.FirstOrDefault(Function(c) c.DeviceId = recordingId)

            ' 自動化
            If My.Settings.AutoRun Then
                AutoRun.SelectedIndex = 0      'True
            Else
                AutoRun.SelectedIndex = 1      'False
            End If

            ' 開機自啟
            AutoStartupComboBox.SelectedIndex = If(My.Settings.AutoStartup, 0, 1)

            ' 靜默啟動
            SilentStartComboBox.SelectedIndex = If(My.Settings.SilentStart, 0, 1)

        Finally
            _suppressEvents = False
        End Try

    End Sub

    Private Function GetAvailableCameras(currentRow As CameraRow) As List(Of CameraInfo)

        Dim all = CameraManager.GetCachedCameras()

        Dim used = CameraRows.
        Where(Function(r) r.SelectedCamera IsNot Nothing AndAlso r IsNot currentRow).
        Select(Function(r) r.SelectedCamera.DeviceId).
        ToList()

        Return all.
        Where(Function(c) Not used.Contains(c.DeviceId)).
        ToList()

    End Function

    Private Sub UpdateCameraButtons()

        For i = 0 To CameraRows.Count - 1

            CameraRows(i).Title = $"相機 {i + 1}"

            ' ➕ 永遠只在最後一個
            CameraRows(i).AddVisible =
            If(i = CameraRows.Count - 1,
               Visibility.Visible,
               Visibility.Collapsed)

            ' ➖ 第一個永遠不能刪
            CameraRows(i).RemoveVisible =
            If(i = 0,
               Visibility.Collapsed,
               Visibility.Visible)

        Next

    End Sub
    ' 新增相機
    Private Sub AddCamera()
        Dim list = CameraManager.GetCachedCameras()

        CameraRows.Add(New CameraRow With {
        .Title = $"相機 {CameraRows.Count + 1}",
        .CameraList = list
    })

        UpdateCameraButtons()
        SaveCameraRows()
    End Sub

    ' 相機控件刪減
    Private Sub AddCamera_Click(sender As Object, e As RoutedEventArgs)
        AddCamera()
    End Sub

    Private Sub RemoveCamera_Click(sender As Object, e As RoutedEventArgs)

        Dim btn = DirectCast(sender, Button)
        Dim row = TryCast(btn.DataContext, CameraRow)

        If row Is Nothing Then Return

        RemoveCamera(row)

    End Sub

    Private Sub RemoveCamera(row As CameraRow)

        If CameraRows.Count <= 1 Then Return

        CameraRows.Remove(row)

        UpdateCameraButtons()
        SaveCameraRows()
    End Sub

    Private Sub SaveCameraRows()
        Dim ids As New System.Collections.Specialized.StringCollection()

        For Each row In CameraRows
            If row.SelectedCamera IsNot Nothing Then
                ids.Add(row.SelectedCamera.DeviceId)
            End If
        Next

        My.Settings.CameraDeviceIds = ids
        My.Settings.Save()
    End Sub

    Private Sub SaveResolutionRows()
        Dim resolutions As New System.Collections.Specialized.StringCollection()

        For Each row In CameraRows
            resolutions.Add(If(row.SelectedResolution?.Tag, ""))
        Next

        My.Settings.CameraResolutions = resolutions
        My.Settings.Save()
    End Sub

    Private Sub RefreshAllCameraLists()

        For Each row In CameraRows
            row.CameraList = GetAvailableCameras(row)
        Next

    End Sub

    Private Sub AutoRunComboBox_SelectionChanged(
    sender As Object,
    e As SelectionChangedEventArgs)

        If Not _isLoaded Then Return
        Select Case AutoRun.SelectedIndex
            Case 0
                My.Settings.AutoRun = True
            Case Else
                My.Settings.AutoRun = False
        End Select

        My.Settings.Save()

    End Sub

    Private Sub GpuBoostComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

    End Sub

    Private Sub RecordingCameraComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If Not _isLoaded Then Return

        Dim cam = TryCast(RecordingCameraComboBox.SelectedItem, CameraInfo)
        My.Settings.RecordingCameraId = cam?.DeviceId
        My.Settings.Save()
    End Sub

    Private Sub AutoStartupComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If Not _isLoaded Then Return
        Dim enabled = (AutoStartupComboBox.SelectedIndex = 0)
        My.Settings.AutoStartup = enabled
        My.Settings.Save()
        ApplyAutoStartup(enabled)
    End Sub

    Private Sub SilentStartComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If Not _isLoaded Then Return
        My.Settings.SilentStart = (SilentStartComboBox.SelectedIndex = 0)
        My.Settings.Save()
    End Sub

    ''' <summary>
    ''' 向 Windows 登錄檔增加/移除開機自啟項目
    ''' </summary>
    Private Sub ApplyAutoStartup(enable As Boolean)
        Try
            Dim appName = "MeteorSystem"
            Dim exePath = System.Reflection.Assembly.GetExecutingAssembly().Location
            ' .dll → .exe for publish scenarios
            If exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) Then
                exePath = IO.Path.ChangeExtension(exePath, ".exe")
            End If

            Using key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                "SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable:=True)
                If enable Then
                    key.SetValue(appName, $"""{exePath}""")
                Else
                    If key.GetValue(appName) IsNot Nothing Then
                        key.DeleteValue(appName)
                    End If
                End If
            End Using
        Catch ex As Exception
            Logger.Warn($"[Setting] 開機自啟設定失敗: {ex.Message}")
        End Try
    End Sub
End Class
