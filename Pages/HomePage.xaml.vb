Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Windows
Imports System.Windows.Threading
Imports MetroSystemForDotNet
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Win32
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Imports Cv = OpenCvSharp

Partial Class HomePage

    Private _initialized As Boolean = False

    Private _currentMat As Mat

    Private _isAlive As Boolean = True
    Private _isActive As Boolean = False
    Private _isStreaming As Boolean = False
    Private _wasStreamingBeforeUnload As Boolean = False

    ' 實時預覽匹配快取
    Private _currentCachedGroupPath As String = ""
    Private _currentCachedSubTemplates As New List(Of Tuple(Of Object, Mat))()
    Private _renderCacheLock As New Object()

    Private _detectLock As New Object()
    Private _isDetecting As Boolean = False
    Private _isPaused As Boolean = False
    Private _isMatchCameraPreWarmed As Boolean = False
    Private _isMatchingInPreview As Boolean = False ' 防止實時匹配任務重疊雪崩
    Private Enum DetectionFlowStage
        Idle = 0
        Matching = 1
        Barcode = 2
        Ocr = 3
    End Enum

    Private _flowStage As DetectionFlowStage = DetectionFlowStage.Idle
    Private _skipCurrentStageRequested As Boolean = False
    Private _activeDetectionResult As DetectionResult
    Private _activeDetectionItem As DetectionItem

    Private Const VoicePromptMatchCompleteScan As String = "MatchCompletedPleaseScan.wav"
    Private Const VoicePromptDecodeCompleteOcr As String = "DecodeCompletedPleaseOCR.wav"
    Private Const VoicePromptSingleFlowCompleted As String = "SingleFlowCompleted.wav"
    Private Const VoicePromptStageTimeout As String = "StageTimeout.wav"                    ' 階段超時
    Private Const VoicePromptStageSkipped As String = "StageSkipped.wav"                    ' 階段已跳過
    Private Const VoicePromptStageRecover As String = "StageRecover.wav"                    ' 收到信號 2：恢復錄製與檢測
    Private Const VoicePromptDetectionPaused As String = "DetectionPaused.wav"              ' 收到信號 1：暫停錄製與檢測
    Private Const VoicePromptDetectionReady As String = "DetectionReady.wav"                ' 收到信號 0：相機就緒，可以開始檢測
    Private Const VoicePromptCorrect As String = "Correct.wav"                              ' 正確（解碼/OCR 成功）
    Private Const VoicePromptError As String = "NoTemplate.wav"
    Private Const VoicePromptNoTemplate As String = "Error.wav"                             ' 找不到供應商對應模板時播報
    Private Const StageTimeoutMs As Integer = 60000
    Private Const StageLoopDelayMs As Integer = 30

    ' 優先使用設定中儲存的相機，否則使用第一個相機
    'Private _detectCameraId As String = If(String.IsNullOrWhiteSpace(My.Settings.CameraDeviceId), GetCamId(0), My.Settings.CameraDeviceId)
    'Private _ocrCameraId As String = If(String.IsNullOrWhiteSpace(My.Settings.CameraDeviceId), GetCamId(0), My.Settings.CameraDeviceId)

    Private _io As IOController

    Private _lastFrameMat As Mat
    Private _lastFrameBitmap As BitmapSource

    Private ReadOnly _ocr As PaddleOcrService =
    AppRuntime.OCR

    ' Page Loaded
    Private Async Sub Page_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        AddHandler LanguageManager.LanguageChanged, AddressOf RefreshLanguageUI

        If _initialized Then
            ' 如果是重複返回此頁面，重新註冊 UI 刷新循環，避免畫面凍結
            RemoveHandler CompositionTarget.Rendering, AddressOf UpdateFrame
            AddHandler CompositionTarget.Rendering, AddressOf UpdateFrame

            ' 重新整理相機清單與工作相機變數（防止在設定頁更改後返回首頁未套用）
            Try
                RefreshCameraComboBox()
            Catch ex As Exception
                Logger.Error($"[Camera] 返回首頁刷新相機失敗: {ex.Message}")
            End Try

            ' 重新訂閱相機變更事件
            RemoveHandler CameraManager.CameraChanged, AddressOf OnCameraChanged
            AddHandler CameraManager.CameraChanged, AddressOf OnCameraChanged

            ' 如果先前離開時處於串流狀態，自動恢復相機與串流更新，防止畫面凍結！
            If _wasStreamingBeforeUnload Then
                Logger.Info("[FLOW] 返回首頁，自動恢復相機串流...")
                RemoveHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived
                AddHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

                ' 啟動定位與解碼相機
                If Not String.IsNullOrWhiteSpace(_matchCameraId) Then
                    CameraService.Instance.StartCamera(_matchCameraId)
                End If
                If Not String.IsNullOrWhiteSpace(_ocrCameraId) AndAlso _ocrCameraId <> _matchCameraId Then
                    CameraService.Instance.StartCamera(_ocrCameraId)
                End If
                _isStreaming = True
            End If
            Return
        End If

        _initialized = True

        ' 顯示載入遮罩
        HomeLoadingOverlay.Visibility = Visibility.Visible
        Await Task.Yield() ' 讓 UI 先渲染遮罩

        Try
            Logger.SetWpfRichTextBox(rtbLog)

            ' IO 初始化
            _io = New IOController(
                "192.168.1.117",
                502,
                1,
                0,
                AppRuntime.IoMode,
                Sub(msg) Logger.Info(msg)
            )
            Await _io.InitializeAsync()

            If AppRuntime.IoMode = IoBoardMode.NONE Then
                Logger.Info("IO 已停用，保留語音播報")
            Else
                Logger.Info("IO 初始化完成")
                _io.SetLightYellow()  ' 程式載入後黃燈常亮待機
            End If

            AddHandler ProcessPage.OnRealtimeTrigger, AddressOf RunDetection
            AddHandler Logger.LogReceived, AddressOf GlobalLogReceived

            Try
                _io.StartDIListener(0)
                AddHandler _io.ButtonChanged, AddressOf IoButtonChanged
                Logger.Info("物理按鈕監聽已啟用")
            Catch ex As Exception
                Logger.Error("啟用物理按鈕監聽失敗: " & ex.Message)
            End Try

            ' 顯示與設定頁相同的完整相機列表，並精確分配相機 1 與相機 2 的 ID
            RefreshCameraComboBox()

            ' 訂閱相機變更事件：設定頁儲存相機後立即刷新本頁的相機清單
            RemoveHandler CameraManager.CameraChanged, AddressOf OnCameraChanged
            AddHandler CameraManager.CameraChanged, AddressOf OnCameraChanged

            ' 【核心修復】註冊 WPF 渲染引擎回調，每當 UI 準備繪製下一幀時，自動執行 UpdateFrame
            RemoveHandler CompositionTarget.Rendering, AddressOf UpdateFrame
            AddHandler CompositionTarget.Rendering, AddressOf UpdateFrame
            _isStreaming = True

        Catch ex As Exception
            Logger.Error($"HomePage 初始化失敗: {ex.Message}")
        Finally
            HomeLoadingOverlay.Visibility = Visibility.Collapsed
            Logger.Info("HomePage 已載入並啟用高幀率實時渲染循環")
        End Try
    End Sub

    Private Sub Page_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        ' 儲存當前串流狀態，供返回首頁時自動恢復
        _wasStreamingBeforeUnload = _isStreaming

        ' 【核心修復】離開頁面時必須解除註冊，防止背景線程持續索取已關閉的硬體資源
        RemoveHandler CompositionTarget.Rendering, AddressOf UpdateFrame
        RemoveHandler CameraManager.CameraChanged, AddressOf OnCameraChanged
        RemoveHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

        ' 釋放實時預覽子模板快取記憶體
        SyncLock _renderCacheLock
            For Each item In _currentCachedSubTemplates
                If item.Item2 IsNot Nothing AndAlso Not item.Item2.IsDisposed Then
                    item.Item2.Dispose()
                End If
            Next
            _currentCachedSubTemplates.Clear()
            _currentCachedGroupPath = ""
        End SyncLock

        ' 如果當前正在串流，且背景沒有執行任務錄影，離開頁面時才暫時關閉相機
        If _isStreaming Then
            Dim isRecording = (TaskVideoRecorder.Instance.GetCurrentInfo() IsNot Nothing)
            If Not isRecording Then
                If Not String.IsNullOrWhiteSpace(_matchCameraId) Then
                    CameraService.Instance.StopCamera(_matchCameraId)
                End If
                If Not String.IsNullOrWhiteSpace(_ocrCameraId) AndAlso _ocrCameraId <> _matchCameraId Then
                    CameraService.Instance.StopCamera(_ocrCameraId)
                End If
            End If
            _isStreaming = False
        End If
    End Sub
    Public Sub RefreshLanguageUI()

        BtnLoadImage.Content = LanguageManager.T("Home_BtnLoadImage")
        BtnClear.Content = LanguageManager.T("Home_BtnClear")
        BtnStart.Content = LanguageManager.T("Home_BtnStart")
        BtnStop.Content = LanguageManager.T("Home_BtnStop")
        BtnGetImg.Content = LanguageManager.T("Home_BtnGetImg")
        BtnSave.Content = LanguageManager.T("Home_BtnSave")
        BtnLaplacian.Content = LanguageManager.T("Home_BtnLaplacian")

    End Sub
    Private Sub GlobalLogReceived(level As String, msg As String)

        ' 【死鎖修正】
        ' Logger.WriteToWpfUI 已在 Dispatcher.Invoke 內先操作 RichTextBox，
        ' 完成後才在 UI 執行緒上 RaiseEvent LogReceived。
        ' 若此處用 Dispatcher.Invoke，UI 執行緒得待完自己，造成死鎖。
        ' 改用 BeginInvoke 非同步派送。
        ' 注意：Logger.SetWpfRichTextBox(rtbLog) 已經讓 Logger 直接寫入 rtbLog，
        ' GlobalLogReceived 不需要再重複寫入同一個 RichTextBox。
        ' 如果未來需要態連其他 UI 控件，可在這裡使用 BeginInvoke。

    End Sub
    Public Async Sub ShowRender(mat As Mat)
        If mat Is Nothing OrElse mat.IsDisposed OrElse mat.CvPtr = IntPtr.Zero Then Return
        If _flowStage <> DetectionFlowStage.Matching Then Return

        ' 雙重防重入鎖，避免多個 Task 同時在背景 match 導致 CPU 暴斃
        If _isMatchingInPreview Then Return
        _isMatchingInPreview = True

        Dim templatePath = LastTemplateStore.Load()
        If String.IsNullOrWhiteSpace(templatePath) Then
            _isMatchingInPreview = False
            Return
        End If

        Dim templateName = IO.Path.GetFileName(templatePath)
        Dim data = TemplateCache.GetTemplate(templateName)

        If data Is Nothing Then
            _isMatchingInPreview = False
            Return
        End If

        Dim groupPath = IO.Path.GetDirectoryName(templatePath)
        Dim subTemplatesToUse As New List(Of Tuple(Of Object, Mat))()

        SyncLock _renderCacheLock
            Try
                If Not String.Equals(_currentCachedGroupPath, groupPath, StringComparison.OrdinalIgnoreCase) Then
                    For Each item In _currentCachedSubTemplates
                        If item.Item2 IsNot Nothing AndAlso Not item.Item2.IsDisposed Then
                            item.Item2.Dispose()
                        End If
                    Next
                    _currentCachedSubTemplates.Clear()

                    Dim subTemplateMetas = TemplateTrainingStore.GetTrainingSamples(groupPath)
                    If subTemplateMetas IsNot Nothing Then
                        For Each subMeta In subTemplateMetas
                            Dim subMat = TemplateTrainingStore.LoadTrainingSampleImage(groupPath, subMeta.FileName)
                            If subMat IsNot Nothing Then
                                _currentCachedSubTemplates.Add(Tuple.Create(DirectCast(subMeta, Object), subMat))
                            End If
                        Next
                    End If
                    _currentCachedGroupPath = groupPath
                End If

                For Each item In _currentCachedSubTemplates
                    subTemplatesToUse.Add(Tuple.Create(item.Item1, item.Item2.Clone()))
                Next
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"[Render] 準備子模板快取失敗: {ex.Message}")
            End Try
        End SyncLock

        Dim matCopyForMatch As Mat = Nothing
        Dim bestResult As Draw_opencv.ResultPack = Nothing
        Dim bestScore As Double = 0

        Try
            matCopyForMatch = mat.Clone()

            ' 1. 執行母版匹配
            Dim masterResult = Await Draw_opencv.ProcessAsync(matCopyForMatch, data.Template, data.Config)
            If masterResult IsNot Nothing Then
                bestResult = masterResult
                bestScore = masterResult.Score
            End If

            ' 2. 母版未 OK 時，比對子模板
            If (bestResult Is Nothing OrElse Not bestResult.IsOk) AndAlso subTemplatesToUse.Count > 0 Then
                For Each item In subTemplatesToUse
                    Dim subMeta = item.Item1
                    Dim subMat = item.Item2
                    Dim subConfig As New TemplateConfig With {
                    .Threshold = If(subMeta.MasterThreshold > 0, subMeta.MasterThreshold, data.Config.Threshold),
                    .MatchMethod = subMeta.MatchMethod,
                    .PyramidLevel = subMeta.PyramidLevel,
                    .CannyLow = If(subMeta.CannyLow > 0, subMeta.CannyLow, data.Config.CannyLow),
                    .CannyHigh = If(subMeta.CannyHigh > 0, subMeta.CannyHigh, data.Config.CannyHigh),
                    .RoiX = data.Config.RoiX, .RoiY = data.Config.RoiY,
                    .RoiW = data.Config.RoiW, .RoiH = data.Config.RoiH
                }

                    Dim subResult = Await Draw_opencv.ProcessAsync(matCopyForMatch, subMat, subConfig)
                    If subResult IsNot Nothing Then
                        If bestResult Is Nothing OrElse (subResult.IsOk AndAlso Not bestResult.IsOk) OrElse (subResult.Score > bestScore) Then
                            If bestResult IsNot Nothing AndAlso bestResult.Mat IsNot Nothing Then
                                bestResult.Mat.Dispose()
                            End If
                            bestResult = subResult
                            bestScore = subResult.Score
                        Else
                            subResult.Mat?.Dispose()
                        End If
                        If subResult.IsOk Then Exit For
                    End If
                Next
            End If

            ' 釋放複製影格
            If matCopyForMatch IsNot Nothing AndAlso Not matCopyForMatch.IsDisposed Then
                matCopyForMatch.Dispose()
                matCopyForMatch = Nothing
            End If

            ' 3. Render 渲染繪製
            If _flowStage = DetectionFlowStage.Matching AndAlso bestResult IsNot Nothing Then
                If bestResult.Mat IsNot Nothing AndAlso Not bestResult.Mat.IsDisposed AndAlso bestResult.Mat.CvPtr <> IntPtr.Zero Then
                    Using display = bestResult.Mat.Clone()
                        RenderImage.Source = display.ToWriteableBitmap()
                    End Using
                    bestResult.Mat.Dispose()
                End If
            End If

        Catch ex As Exception
            Logger.Error($"ShowRender 異常: {ex.Message}")
        Finally
            ' 確保所有資源安全釋放，防堵記憶體洩漏
            _isMatchingInPreview = False
            If matCopyForMatch IsNot Nothing AndAlso Not matCopyForMatch.IsDisposed Then
                matCopyForMatch.Dispose()
            End If
            For Each item In subTemplatesToUse
                If item.Item2 IsNot Nothing AndAlso Not item.Item2.IsDisposed Then
                    item.Item2.Dispose()
                End If
            Next
            subTemplatesToUse.Clear()
        End Try
    End Sub
    ' 實時畫面流回調
    Private Sub UpdateFrame(sender As Object, e As EventArgs)
        Dim targetCamId As String = _matchCameraId

        If _flowStage = DetectionFlowStage.Barcode OrElse _flowStage = DetectionFlowStage.Ocr Then
            targetCamId = _ocrCameraId
        End If

        If String.IsNullOrWhiteSpace(targetCamId) Then Return

        Try
            Dim frame = CameraService.Instance.GetFrame(targetCamId)
            If frame Is Nothing Then Return

            If _flowStage = DetectionFlowStage.Matching Then
                ' 如果背景匹配任務還在執行，直接把原生畫面丟給 UI（維持 60 FPS 預覽），不進行昂貴的 Mat 轉換與比對
                If Not _isMatchingInPreview Then
                    Dim oldMat = _lastFrameMat
                    _lastFrameMat = frame.ToMat()

                    If oldMat IsNot Nothing AndAlso Not oldMat.IsDisposed Then
                        oldMat.Dispose()
                    End If

                    If _lastFrameMat IsNot Nothing AndAlso Not _lastFrameMat.IsDisposed Then
                        ShowRender(_lastFrameMat)
                    End If
                Else
                    ' 匹配忙碌中，只更新畫面，不跑演算法
                    RenderImage.Source = frame
                End If
            Else
                ' 非匹配階段，直接顯示
                RenderImage.Source = frame
            End If

        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine($"UpdateFrame 同步與轉換發生異常: {ex.Message}")
        End Try
    End Sub
    Private Sub BtnClearLog_Click(sender As Object, e As RoutedEventArgs)
        ' 將 Document 設為新的 FlowDocument 即達成了清空效果
        rtbLog.Document = New FlowDocument()
    End Sub
    ''' <summary>
    ''' 從 CameraManager 快取重新載入相機清單並更新 ComboBox，
    ''' 盡量保留使用者當前的選擇（若該相機仍存在於清單中）。
    ''' 必須在 UI 執行緒呼叫。
    ''' </summary>
    ' 變更為精確對應：相機 1 (定位)、相機 2 (OCR/解碼)
    Private _matchCameraId As String = ""
    Private _ocrCameraId As String = ""

    Private Sub RefreshCameraComboBox()
        Dim allCameras = CameraManager.GetCachedCameras()
        If allCameras Is Nothing OrElse allCameras.Count = 0 Then Return

        CameraComboBox.ItemsSource = allCameras

        ' ─── 精確對接 SettingPage 儲存的多相機陣列 ───
        Dim savedIds = My.Settings.CameraDeviceIds

        If savedIds IsNot Nothing AndAlso savedIds.Count > 0 Then
            ' 相機 1 用於定位匹配
            _matchCameraId = savedIds(0)

            ' 相機 2 用於條碼與 OCR（如果使用者有設定第二台的話，否則 fallback 到第一台）
            If savedIds.Count > 1 Then
                _ocrCameraId = savedIds(1)
            Else
                _ocrCameraId = savedIds(0)
            End If
        Else
            ' Fallback 機制
            _matchCameraId = allCameras(0).DeviceId
            _ocrCameraId = allCameras(0).DeviceId
        End If

        ' 實時檢測界面上的 ComboBox 顯示目前正在工作的相機（依階段決定）
        Dim currentWorkingId = If(_flowStage = DetectionFlowStage.Ocr OrElse _flowStage = DetectionFlowStage.Barcode, _ocrCameraId, _matchCameraId)
        Dim toSelect = allCameras.FirstOrDefault(Function(c) String.Equals(c.DeviceId, currentWorkingId, StringComparison.OrdinalIgnoreCase))

        CameraComboBox.SelectedItem = toSelect
        Logger.Info($"[Camera] 實時頁面相機載入成功。定位相機：{_matchCameraId}，OCR相機：{_ocrCameraId}")
    End Sub

    ''' <summary>
    ''' 設定頁儲存相機設定後觸發（CameraManager.CameraChanged），
    ''' 在 UI 執行緒重新整理相機清單，讓變更立即生效，不需要重開此頁。
    ''' </summary>
    ' 設定頁變更後的動態套用
    Private Sub OnCameraChanged()
        ' 【死鎖修正】
        ' CameraChanged 事件可能直接在 UI 執行緒上被呼叫（例如從 CameraComboBox_SelectionChanged）。
        ' 若此時使用 Dispatcher.Invoke（同步），UI 執行緒會等待自己完成，造成死鎖。
        ' 改用 Dispatcher.BeginInvoke（非同步派送）讓 UI 執行緒立即返回，避免死鎖。
        Dispatcher.BeginInvoke(Sub()
                                   Try
                                       RefreshCameraComboBox() ' 更新本頁的 _matchCameraId 與 _ocrCameraId 變數
                                   Catch ex As Exception
                                       Logger.Error($"[Camera] 刷新相機清單失敗: {ex.Message}")
                                   End Try
                               End Sub)

        Task.Run(Sub()
                     Dim ids = My.Settings.CameraDeviceIds
                     If ids Is Nothing OrElse ids.Count = 0 Then
                         CameraService.Instance.StopAll()
                         Return
                     End If

                     ' 先關閉所有相機，乾淨釋放硬體資源
                     CameraService.Instance.StopAll()
                     System.Threading.Thread.Sleep(300)

                     ' 重新啟動相機 1 (定位)
                     Dim cam1 = ids(0)
                     If Not String.IsNullOrWhiteSpace(cam1) Then
                         CameraService.Instance.StartCamera(cam1)
                         Logger.Info($"[Camera] 定位相機已啟動: {cam1}")
                     End If

                     ' 重新啟動相機 2 (OCR/解碼)（如果存在且與相機 1 不同設備）
                     If ids.Count > 1 Then
                         Dim cam2 = ids(1)
                         If Not String.IsNullOrWhiteSpace(cam2) Then
                             CameraService.Instance.StartCamera(cam2)
                             Logger.Info($"[Camera] OCR/解碼相機已啟動: {cam2}")
                         End If
                     End If
                 End Sub)
    End Sub
End Class
