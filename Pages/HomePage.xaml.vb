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

    Private _detectLock As New Object()
    Private _isDetecting As Boolean = False

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
    Private Const VoicePromptStageTimeout As String = "StageTimeout.wav"
    Private Const StageTimeoutMs As Integer = 60000
    Private Const StageLoopDelayMs As Integer = 30

    Private _detectCameraId As String = GetCamId(1)
    Private _ocrCameraId As String = GetCamId(1)

    Private _io As IOController

    Private _lastFrameMat As Mat
    Private _lastFrameBitmap As BitmapSource

    If Not String.IsNullOrWhiteSpace(fallback) Then
            _detectCameraId = fallback
            _ocrCameraId = fallback
        End If

    Return fallback
    End Function

    Private Async Function GetDetectFrameAsync() As Task(Of BitmapSource)
        Dim camId = ResolveDetectCameraId()
        If String.IsNullOrWhiteSpace(camId) Then
            Logger.Error("[DETECT] 未設定檢測相機")
            Return Nothing
        End If

        Dim frame = CameraService.Instance.GetFrame(camId)
        If frame IsNot Nothing Then Return frame

        CameraService.Instance.StartCamera(camId)

        For i As Integer = 1 To 20
            Await Task.Delay(50)
            frame = CameraService.Instance.GetFrame(camId)
            If frame IsNot Nothing Then Return frame
        Next

        Logger.Error($"[DETECT] 無法取得相機畫面，CamId={camId}")
        Return Nothing
    End Function

    Private Function TryBeginDetectionSession(triggerSource As String, ByRef stage As DetectionFlowStage) As Boolean
        SyncLock _detectLock
            If Not _isDetecting Then
                _isDetecting = True
                _skipCurrentStageRequested = False
                _flowStage = DetectionFlowStage.Matching
                _activeDetectionResult = New DetectionResult With {
                    .List = New List(Of DetectionItem)
                }
                _activeDetectionItem = New DetectionItem With {
                    .detectionNo = $"AI-{DateTime.Now:yyyyMMdd-HHmmssfff}"
                }
                _activeDetectionResult.List.Add(_activeDetectionItem)
                Logger.Info($"[{triggerSource}] 啟動新的檢測組")
                stage = _flowStage
                Return True
            End If

            stage = _flowStage
            Logger.Info($"[{triggerSource}] 忽略重入觸發，當前階段={stage}")
            Return False
        End SyncLock
    End Function

    Private Sub SetFlowStage(stage As DetectionFlowStage)
        SyncLock _detectLock
            _flowStage = stage
            _skipCurrentStageRequested = False
        End SyncLock
    End Sub

    Private Function IsSkipRequested(stage As DetectionFlowStage) As Boolean
        SyncLock _detectLock
            Return _flowStage = stage AndAlso _skipCurrentStageRequested
        End SyncLock
    End Function

    Private Sub FinishDetection()
        _io.SetLightYellow() ' 流程結束後黃燈常亮待機
        SyncLock _detectLock
            _isDetecting = False
            _skipCurrentStageRequested = False
            _flowStage = DetectionFlowStage.Idle
            _activeDetectionResult = Nothing
            _activeDetectionItem = Nothing
        End SyncLock
    End Sub

    Private Function IsSkippableStageRunning() As Boolean
        SyncLock _detectLock
            Return _isDetecting
        End SyncLock
    End Function

    Public Sub StopTaskFlow()
        Try
            FinishDetection()
            CameraService.Instance.StopAll()
            _isStreaming = False
            Logger.Info("[FLOW] 任務結束，已停止流程與相機")
        Catch ex As Exception
            Logger.Error("[FLOW] 停止流程失敗: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 暂停检测流程和倒计时
    ''' </summary>
    Public Sub PauseDetectionFlow()
        Try
            SyncLock _detectLock
                ' 如果检测正在进行，设置暂停标志
                Logger.Info("[FLOW] 检测流程已暂停")
                ' 播报语音提示："检测已暂停"
                PlayPromptVoice("DetectionPaused.wav")
            End SyncLock
        Catch ex As Exception
            Logger.Error("[FLOW] 暂停流程失敗: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 恢复检测流程和倒计时
    ''' </summary>
    Public Sub ResumeDetectionFlow()
        Try
            SyncLock _detectLock
                ' 恢复检测流程
                Logger.Info("[FLOW] 检测流程已恢复")
                ' 播报语音提示："检测已恢复"
                PlayPromptVoice("DetectionResumed.wav")
            End SyncLock
        Catch ex As Exception
            Logger.Error("[FLOW] 恢复流程失敗: " & ex.Message)
        End Try
    End Sub

    Private Sub FillImageBase64(result As DetectionResult)
        If result Is Nothing OrElse result.Mat Is Nothing Then Return

        Dim bmp = MatToBitmapSource(result.Mat)
        Dim encoder As New PngBitmapEncoder()
        encoder.Frames.Add(BitmapFrame.Create(bmp))

        Using ms As New IO.MemoryStream()
            encoder.Save(ms)
            result.ImageBase64 = Convert.ToBase64String(ms.ToArray())
        End Using
    End Sub

    Public Async Function RunDetection(callback As Action(Of DetectionResult)) As Task

        Try
            Logger.Debug($"[DETECT ENTER] {Guid.NewGuid()}")

            Dim result = Await BtnGetImg_Click()
            If result Is Nothing Then Return

            If result.IsFinal Then
                FillImageBase64(result)
                callback(result)
            End If

        Catch ex As Exception
            Logger.Error("RunDetection error: " & ex.Message)
        End Try

    End Function

    Public Async Function RunDetectionOnce() As Task(Of DetectionResult)

        Try
            Dim result = Await BtnGetImg_Click()
            If result Is Nothing Then Return Nothing

            If result.IsFinal Then
                FillImageBase64(result)
            End If

            Return result

        Catch ex As Exception

            Logger.Error(ex.Message)
            Return Nothing

        End Try

    End Function
    ' Page Loaded
    Private Async Sub Page_Loaded(
        sender As Object,
        e As RoutedEventArgs) Handles Me.Loaded
        AddHandler LanguageManager.LanguageChanged, AddressOf RefreshLanguageUI

        If _initialized Then
            ' ⭐ 回來時補訂閱（關鍵）
            If _isStreaming Then
                AddHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived
            End If

            Return
        End If

        _initialized = True

        Logger.SetWpfRichTextBox(rtbLog)

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
        End If
        ' 數據交互訂閲

        AddHandler ProcessPage.OnRealtimeTrigger, AddressOf RunDetection

        AddHandler Logger.LogReceived, AddressOf GlobalLogReceived

        ' 啟用並訂閱物理按鈕（單一按鈕）
        Try
            _io.StartDIListener(0) ' DI index 可根據硬體配置調整
            AddHandler _io.ButtonChanged, AddressOf IoButtonChanged
            Logger.Info("物理按鈕監聽已啟用")
        Catch ex As Exception
            Logger.Error("啟用物理按鈕監聽失敗: " & ex.Message)
        End Try

        Logger.Info("HomePage 已載入")

        ' =========================
        ' 初始化相機選擇 ComboBox
        ' =========================
        Try
            CameraManager.Initialize()
            CameraManager.Refresh()
            Dim cameras = CameraManager.GetCachedCameras()
            If cameras IsNot Nothing AndAlso cameras.Count > 0 Then
                CameraComboBox.ItemsSource = cameras
                If Not String.IsNullOrWhiteSpace(_detectCameraId) Then
                    CameraComboBox.SelectedValue = _detectCameraId
                ElseIf cameras.Count > 0 Then
                    CameraComboBox.SelectedIndex = 0
                    _detectCameraId = cameras(0).DeviceId
                End If
                Logger.Info($"相機列表已加載，共 {cameras.Count} 個相機")
            Else
                Logger.Warn("未找到可用的相機設備")
            End If
        Catch ex As Exception
            Logger.Error($"初始化相機列表失敗: {ex.Message}")
        End Try

        ' =========================
        ' Live2D Path
        ' =========================
        'Dim live2dPath As String =
        '    Path.Combine(
        '        AppDomain.CurrentDomain.BaseDirectory,
        '        "UI",
        '        "live2d")

        'Logger.Info(
        '    "Live2D SubSysPath: " & live2dPath)

        _isStreaming = False
    End Sub

    ' =========================================
    ' Load Image
    ' =========================================
    Private Async Sub BtnLoadImage_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            Dim path = DialogHelper.OpenImage()
            If String.IsNullOrWhiteSpace(path) Then Return

            Dim mat = Cv.Cv2.ImRead(path)
            _currentMat = mat

            Dim templatePath = LastTemplateStore.Load()
            If String.IsNullOrWhiteSpace(templatePath) Then Return

            Dim templateName = IO.Path.GetFileName(templatePath)

            Dim result = Await Draw_opencv.ProcessAsync(mat, templateName)

            RenderImage.Source =
            result.Mat.ToWriteableBitmap()

            Logger.Info($"Score={result.Score:F3}, OK={result.IsOk}")

            If _io IsNot Nothing Then
                Dim snapshot = TemplateSnapshotStore.Load()
                If snapshot IsNot Nothing Then
                    _io.TriggerByScore(result.Score, snapshot.Threshold)
                End If
            End If

        Catch ex As Exception
            ErrorDialogHelper.ShowError("ROI錯誤: " & ex.Message)
        End Try

    End Sub
#Region "相機觸發"
    Private _io As IOController

    Private Sub PlayPromptVoice(fileName As String)
        If String.IsNullOrWhiteSpace(fileName) Then Return
        If _io Is Nothing Then
            Logger.Warn($"[VOICE] IOController尚未初始化，無法播報: {fileName}")
            Return
        End If
        Try
            Logger.Info($"[VOICE] 播報開始: {fileName}")
            _io.PlayCustomVoice(fileName)
            Logger.Info($"[VOICE] 播報完成: {fileName}")
        Catch ex As Exception
            Logger.Error($"[VOICE] 播報失敗: {ex.Message}")
        End Try
    End Sub

    Private Function ResolveRoi(snapshot As TemplateSnapshot, src As Mat) As OpenCvSharp.Rect
        If src Is Nothing OrElse src.Empty() Then Return New OpenCvSharp.Rect(0, 0, 1, 1)
        If snapshot Is Nothing Then Return New OpenCvSharp.Rect(0, 0, src.Width, src.Height)

        If snapshot.RoiW <= 0 OrElse snapshot.RoiH <= 0 Then
            Return New OpenCvSharp.Rect(0, 0, src.Width, src.Height)
        End If

        Dim x = Math.Max(0, Math.Min(snapshot.RoiX, src.Width - 1))
        Dim y = Math.Max(0, Math.Min(snapshot.RoiY, src.Height - 1))
        Dim w = Math.Max(1, Math.Min(snapshot.RoiW, src.Width - x))
        Dim h = Math.Max(1, Math.Min(snapshot.RoiH, src.Height - y))

        Return New OpenCvSharp.Rect(x, y, w, h)
    End Function

    ' 多次匹配方法：在3秒内每200ms嘗試一次，收集最高分，最後比對閾值決定OK/NG
    Private Async Function WaitMultipleMatchAsync(templatePath As String, snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of MatchResultWrapper)
        Dim sw As New Stopwatch()
        sw.Start()

        ' 直接從路徑加載母版（繞過 TemplateCache，確保加載最新模板）
        Dim masterData = TemplateManager.LoadTemplate(templatePath)
        If masterData Is Nothing OrElse masterData.Template Is Nothing Then
            Logger.Warn($"[FLOW] 無法加載母版模板: {templatePath}")
            Return New MatchResultWrapper With {.Result = Nothing}
        End If

        'Dim cameraId = ResolveDetectCameraId()
        '臨時
        Dim cameraId = GetCamId(0)

        CameraService.Instance.StopAll()
        Thread.Sleep(100)
        CameraService.Instance.StartCamera(cameraId)
        '
        Dim bestResultMat As Cv.Mat = Nothing
        Dim bestScore As Double = 0
        Dim bestThreshold As Double = masterData.Config.Threshold ' 追蹤最高分對應的閾值
        Dim matchAttempt As Integer = 0
        Dim lastAttemptTime As Long = -200 ' 立即觸發第一次匹配

        ' 獲取子模板列表（groupPath = 母版的父目錄，即 test3 資料夾）
        Dim groupPath = IO.Path.GetDirectoryName(templatePath)
        Dim subTemplateMetas = TemplateTrainingStore.GetTrainingSamples(groupPath)
        Logger.Debug($"[MATCH] 子模板數量={If(subTemplateMetas IsNot Nothing, subTemplateMetas.Count, 0)}, groupPath={groupPath}")

        ' 相機啟動一次（在迴圈開始前）
        If Not String.IsNullOrWhiteSpace(cameraId) Then
            CameraService.Instance.StartCamera(cameraId)
        End If

        ' 每200ms嘗試一次（3秒內最多約15次）
        Const AttemptIntervalMs As Long = 200

        While sw.ElapsedMilliseconds < timeoutMs
            If sw.ElapsedMilliseconds - lastAttemptTime >= AttemptIntervalMs Then
                matchAttempt += 1
                lastAttemptTime = sw.ElapsedMilliseconds

                Dim frame As BitmapSource = Nothing
                If Not String.IsNullOrWhiteSpace(cameraId) Then
                    frame = CameraService.Instance.GetFrame(cameraId)
                End If

                If frame IsNot Nothing Then
                    ' 更新預覽畫面
                    Dim frameCopy = frame
                    Dispatcher.Invoke(Sub() RenderImage.Source = frameCopy)

                    Using currentMat = BitmapSourceToMat(frame)
                        Dim gray = currentMat.CvtColor(ColorConversionCodes.BGR2GRAY)
                        Dim meanVal = Cv2.Mean(gray).Val0

                        ' ===== 改進的內容檢驗 =====
                        ' 1. 檢查亮度：過暗或過亮都跳過
                        If meanVal < 15 OrElse meanVal > 245 Then
                            Logger.Warn($"[MATCH] 跳過異常亮度幀: mean={meanVal:F1}")
                            Continue While
                        End If

                        ' 2. 檢查方差：檢測圖像的對比度（區分空白背景）
                        Dim meanScalar As New Cv.Scalar()
                        Dim stdDevScalar As New Cv.Scalar()
                        Cv2.MeanStdDev(gray, meanScalar, stdDevScalar)
                        Dim stdDev = stdDevScalar.Val0

                        ' 方差過低表示圖像過於均勻（如空白背景或單色背景）
                        If stdDev < 10 Then
                            Logger.Warn($"[MATCH] 跳過對比度過低的幀: stdDev={stdDev:F1}")
                            Continue While
                        End If

                        ' 3. 檢查邊緣密度：確保有足夠的邊緣特徵
                        Dim edges As New Cv.Mat()
                        Cv2.Canny(gray, edges, 80, 160)
                        Dim edgeCount = Cv2.CountNonZero(edges)
                        Dim totalPixels = edges.Width * edges.Height
                        Dim edgeDensity = CDbl(edgeCount) / CDbl(totalPixels)

                        BtnLoadImage.Content = LanguageManager.T("Home_BtnLoadImage")
                        BtnClear.Content = LanguageManager.T("Home_BtnClear")
                        BtnStart.Content = LanguageManager.T("Home_BtnStart")
                        BtnStop.Content = LanguageManager.T("Home_BtnStop")
                        BtnGetImg.Content = LanguageManager.T("Home_BtnGetImg")
                        BtnSave.Content = LanguageManager.T("Home_BtnSave")
                        BtnLaplacian.Content = LanguageManager.T("Home_BtnLaplacian")

                        End Sub
    Private Sub GlobalLogReceived(level As String, msg As String)

        Dispatcher.Invoke(Sub()

                              ' 這裡你可以：
                              ' 1. 更新本頁 log
                              ' 2. 或丟到共享 log window

                              lastScore = masterResult.Score
                              End If
                              End If

                              ' 嘗試匹配所有子模板（每個子模板用自己的 params）
                              If subTemplateMetas IsNot Nothing AndAlso subTemplateMetas.Count > 0 Then
                                  For Each subMeta In subTemplateMetas
                                      Dim subMat = TemplateTrainingStore.LoadTrainingSampleImage(groupPath, subMeta.FileName)
                                      If subMat IsNot Nothing Then
                                          Try
                                              ' 從 TrainingSampleMeta 建立此子模板專屬的 TemplateConfig
                                              Dim subConfig As New TemplateConfig With {
                                                  .Threshold = If(subMeta.MasterThreshold > 0, subMeta.MasterThreshold, masterData.Config.Threshold),
                                                  .MatchMethod = subMeta.MatchMethod,
                                                  .PyramidLevel = subMeta.PyramidLevel,
                                                  .CannyLow = If(subMeta.CannyLow > 0, subMeta.CannyLow, masterData.Config.CannyLow),
                                                  .CannyHigh = If(subMeta.CannyHigh > 0, subMeta.CannyHigh, masterData.Config.CannyHigh)
                                              }
                                              Dim subResult = Await Draw_opencv.ProcessAsync(currentMat, subMat, subConfig)
                                        If subResult IsNot Nothing AndAlso subResult.Score > bestScore Then
                                                  bestScore = subResult.Score
                                                  bestThreshold = subConfig.Threshold
                                                  bestResultMat = subResult.Mat
                                                  Logger.Debug($"[MATCH] #{matchAttempt} 子模板'{subMeta.FileName}' Score={subResult.Score:F3} (閾值={subConfig.Threshold:F3})")
                                                  ' 即時渲染匹配結果
                                                  If subResult.Mat IsNot Nothing Then
                                                      Dim wb = subResult.Mat.ToWriteableBitmap()
                                                      Dispatcher.Invoke(Sub() RenderImage.Source = wb)
                                                  End If
                                              End If
                                          Finally
                                              subMat.Dispose()
                                          End Try
                                      End If
                                  Next
                              End If
                              End Using
                              Else
                              Logger.Warn($"[MATCH] #{matchAttempt}: 無法獲取相機畫面")
                              End If
                              End If

                              Await Task.Delay(50)
        End While

                              ' 3秒結束後，用最高分與對應閾值比較決定 OK/NG
                              Dim isOk = (bestScore >= bestThreshold)
                              Logger.Info($"[MATCH] 3秒結束，最高分={bestScore:F3}, 對應閾值={bestThreshold:F3}, IsOk={isOk}, 共{matchAttempt}次")

                              Dim finalResult As New Draw_opencv.ResultPack With {
                                  .Score = bestScore,
                                  .isOk = isOk,
                                  .Mat = bestResultMat
                              }
                              CameraService.Instance.StopCamera(cameraId)
                              Return New MatchResultWrapper With {.Result = finalResult, .MatchCount = matchAttempt}
                              End Function

    ' 匹配结果包装类
    Private Class MatchResultWrapper
        Public Property Result As Draw_opencv.ResultPack
        Public Property MatchCount As Integer = 0
    End Class

    Private Async Function WaitBarcodeResultAsync(snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of String)
        Dim decoder = AppRuntime.Barcode
        If decoder Is Nothing Then Return ""

        'Dim cameraId = ResolveDetectCameraId()
        '臨時
        Dim cameraId = GetCamId(1)
        '
        If String.IsNullOrWhiteSpace(cameraId) Then Return ""
        ' 臨時
        CameraService.Instance.StopAll()
        Thread.Sleep(100)
        '
        CameraService.Instance.StartCamera(cameraId)

        Dim sw As New Stopwatch()
        sw.Start()

        ' 50ms 一次，最大化解碼頻率
        Const DecodeIntervalMs As Long = 50
        Dim lastAttempt As Long = -DecodeIntervalMs
        Dim decoding As Boolean = False      ' 防止前一幀還未解完就重疊
        Dim resultBox As String = Nothing    ' 跨 Task 傳遞結果

        While sw.ElapsedMilliseconds < timeoutMs

            If IsSkipRequested(DetectionFlowStage.Barcode) Then
                Logger.Info("[FLOW] 解碼已跳過")
                Return ""
            End If

            ' 有結果立即返回
            If resultBox IsNot Nothing Then
                Logger.Info($"[FLOW] 解碼成功: {resultBox}")
                Return resultBox
            End If

            Dim elapsed = sw.ElapsedMilliseconds
            If Not decoding AndAlso elapsed - lastAttempt >= DecodeIntervalMs Then
                lastAttempt = elapsed
                Dim frame = CameraService.Instance.GetFrame(cameraId)

                If frame IsNot Nothing Then
                    ' 更新預覽（不阻塞）
                    Dim frameCopy = frame
                    Dispatcher.BeginInvoke(Sub() RenderImage.Source = frameCopy)

                    ' 解碼在執行緒池，不 await，用 flag 防重疊
                    decoding = True
                    Task.Run(Function()
                                 Try
                                     Using mat = BitmapSourceToMat(frameCopy)
                                         ' 1. 先全畫面解碼（位置無關）
                                         Dim text = decoder.Run(mat)

                                         ' 2. 全畫面失敗時，若有設定 ROI 再縮小範圍嘗試
                                         If String.IsNullOrWhiteSpace(text) Then
                                             Dim roi = ResolveRoi(snapshot, mat)
                                             Dim isFullFrame = (roi.X = 0 AndAlso roi.Y = 0 AndAlso
                                                                roi.Width = mat.Width AndAlso roi.Height = mat.Height)
                                             If Not isFullFrame Then
                                                 text = decoder.RunRoi(mat, roi)
                                             End If
                                         End If

                                         ' 3. 若仍未解碼，嘗試多角度解碼和增強預處理
                                         If String.IsNullOrWhiteSpace(text) Then
                                             text = TryAdvancedBarcodeDecode(decoder, mat, snapshot)
                                         End If

                                         If Not String.IsNullOrWhiteSpace(text) Then
                                             resultBox = text.Trim()
                                             Logger.Debug($"[BARCODE] 解碼成功: {text}")
                                         End If
                                     End Using
                                 Catch ex As Exception
                                     Logger.Warn("[FLOW] 解碼異常: " & ex.Message)
                                 Finally
                                     decoding = False
                                 End Try
                                 Return True
                             End Function)
                End If
            End If

            Await Task.Delay(10) ' UI 呼吸間隔，不影響解碼頻率
        End While

        Logger.Warn("[FLOW] 解碼超時")
        Return ""
    End Function

    ''' <summary>
    ''' 高級條碼解碼：考慮多角度、小的/模糊的目標
    ''' 策略：1)對比度增強 2)多角度嘗試 3)縮放處理小目標 4)適應性二值化
    ''' </summary>
    Private Function TryAdvancedBarcodeDecode(decoder As Object, mat As Mat, snapshot As TemplateSnapshot) As String
        Try
            ' 策略1：對比度增強（CLAHE）
            Dim enhanced = EnhanceContrast(mat)
            If enhanced IsNot Nothing Then
                Try
                    Dim result = decoder.Run(enhanced)
                    If Not String.IsNullOrWhiteSpace(result) Then
                        Logger.Debug("[BARCODE] 通過對比度增強成功解碼")
                        Return result
                    End If
                Finally
                    enhanced.Dispose()
                End Try
            End If

            ' 策略2：多角度嘗試（±15°, ±30°）
            Dim angles As Integer() = {-30, -15, 15, 30}
            For Each angle In angles
                Dim rotated = RotateImage(mat, angle)
                If rotated IsNot Nothing Then
                    Try
                        Dim result = decoder.Run(rotated)
                        If Not String.IsNullOrWhiteSpace(result) Then
                            Logger.Debug($"[BARCODE] 通過旋轉 {angle}° 成功解碼")
                            Return result
                        End If
                    Finally
                        rotated.Dispose()
                    End Try
                End If
            Next

            ' 策略3：上採樣以提高小目標可讀性（2x 放大）
            Dim upscaled = UpscaleImage(mat, 2.0)
            If upscaled IsNot Nothing Then
                Try
                    Dim result = decoder.Run(upscaled)
                    If Not String.IsNullOrWhiteSpace(result) Then
                        Logger.Debug("[BARCODE] 通過上採樣 (2x) 成功解碼")
                        Return result
                    End If

                    ' 上採樣後也嘗試對比度增強
                    Dim enhancedUpscaled = EnhanceContrast(upscaled)
                    If enhancedUpscaled IsNot Nothing Then
                        Try
                            result = decoder.Run(enhancedUpscaled)
                            If Not String.IsNullOrWhiteSpace(result) Then
                                Logger.Debug("[BARCODE] 通過上採樣+對比度增強成功解碼")
                                Return result
                            End If
                        Finally
                            enhancedUpscaled.Dispose()
                        End Try
                    End If
                Finally
                    upscaled.Dispose()
                End Try
            End If

            End Sub)

    End Sub
    ' =========================================
    ' Show Render
    ' =========================================
    Public Async Sub ShowRender(mat As Mat)

        If mat Is Nothing Then Return

        Try

            ' =========================
            ' Template Name
            ' =========================
            Dim templatePath = LastTemplateStore.Load()

            If String.IsNullOrWhiteSpace(templatePath) Then
                RenderImage.Source = mat.ToWriteableBitmap()
                Return
            End If

            Dim templateName = IO.Path.GetFileName(templatePath)

            ' =========================
            ' Get Template From Cache
            ' =========================
            Dim data = TemplateCache.GetTemplate(templateName)

            If data Is Nothing Then
                Logger.Warn($"模板不存在: {templateName}")
                RenderImage.Source = mat.ToWriteableBitmap()
                Return
            End If

            ' =========================
            ' Match (Async)
            ' =========================
            Dim result = Await TemplateMatcher.MatchAsync(
            mat,
            data.Template,
            data.Config.Threshold,
            data.Config.MatchMethod)

            If result Is Nothing Then Return

            ' =========================
            ' Render Overlay (畫框 + 分數)
            ' =========================
            Dim display = result.ResultImage.Clone()

            ' --- score text ---
            Dim text As String =
            $"Score: {result.Score:F3}"

            Dim org As New OpenCvSharp.Point(20, 40)

            Cv2.PutText(
            display,
            text,
            org,
            HersheyFonts.HersheySimplex,
            1.0,
            Scalar.Yellow,
            2)

            ' =========================
            ' UI Update
            ' =========================
            RenderImage.Source =
            display.ToWriteableBitmap()

            ' =========================
            ' Log
            ' =========================
            Logger.Info(
            $"Match OK={result.IsOk}, Score={result.Score:F3}")

        Catch ex As Exception

            Logger.Error($"ShowRender error: {ex.Message}")
            RenderImage.Source = mat.ToWriteableBitmap()

        End Try

    End Sub

    Private Sub GlobalLogReceived(level As String, msg As String)

        Dispatcher.Invoke(Sub()

                              ' 這裡你可以：
                              ' 1. 更新本頁 log
                              ' 2. 或丟到共享 log window

                              rtbLog.AppendText($"[{level}] {msg}" & Environment.NewLine)
                              rtbLog.ScrollToEnd()

                          End Sub)

    End Sub
    Private _lastFrameMat As Mat
    Private _lastFrameBitmap As BitmapSource
    Private Sub OnFrameArrived(deviceId As String, img As BitmapSource)

        If RenderImage Is Nothing Then Return
        If Not String.Equals(deviceId, _detectCameraId, StringComparison.OrdinalIgnoreCase) Then Return

        RenderImage.Dispatcher.BeginInvoke(Sub()

                                               If RenderImage Is Nothing Then Return

                                               RenderImage.Source = img

                                               ' ⭐ 保存最後一幀（UI層）
                                               _lastFrameBitmap = img

                                           End Sub)
    End Sub
    Private Sub Page_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        RemoveHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

    End Sub

    Private Sub BtnStart_Click(sender As Object, e As RoutedEventArgs)

        Dim frame = CameraService.Instance.GetFrame(GetCamId(1))

        If frame Is Nothing Then Return

        RenderImage.Source = frame

    End Sub
    Private Sub OnCameraChanged()

        Task.Run(Sub()

                     Dim ids = My.Settings.CameraDeviceIds
                     If ids Is Nothing OrElse ids.Count = 0 Then
                         CameraService.Instance.StopAll()
                         Return
                     End If

                     CameraService.Instance.StopAll()

                     ' 重新解析當前檢測相機——用最新設定中的第一個相機
                     Dim newCameraId = GetCamId(1)
                     If Not String.IsNullOrWhiteSpace(newCameraId) Then
                         _detectCameraId = newCameraId
                         CameraService.Instance.StartCamera(newCameraId)
                         Logger.Info($"[Camera] 設定已更新，相機切換為: {newCameraId}")
                     End If

                 End Sub)
    End Sub
End Class
