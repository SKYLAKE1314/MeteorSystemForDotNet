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
    Private Function TryBeginDetectionSession(triggerSource As String, ByRef stage As DetectionFlowStage) As Boolean
        SyncLock _detectLock
            If Not _isDetecting Then
                _isDetecting = True
                _isPaused = False
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

        ' 【核心修復】階段切換時，強制回主執行緒刷新相機 ComboBox
        ' 這會讓 UI 立即顯示當下正在工作（OCR/Barcode）的相機，並確保相機狀態同步
        Application.Current.Dispatcher.Invoke(Sub()
                                                  Try
                                                      RefreshCameraComboBox()
                                                      Logger.Info($"[FLOW] 階段切換至 {stage.ToString()}，已同步切換工作相機")
                                                  Catch ex As Exception
                                                      Logger.Error($"[FLOW] 切換相機 UI 失敗: {ex.Message}")
                                                  End Try
                                              End Sub)
    End Sub

    Private Function IsSkipRequested(stage As DetectionFlowStage) As Boolean
        SyncLock _detectLock
            Return _flowStage = stage AndAlso _skipCurrentStageRequested
        End SyncLock
    End Function

    Private Sub FinishDetection()
        _io.SetLightYellow()
        SyncLock _detectLock
            _isDetecting = False
            _isPaused = False
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
    ''' 收到 status 0 時提前啟動匹配相機，確保按鈕觸發時相機已有畫面
    ''' </summary>
    Public Sub PreWarmMatchCamera()
        Try
            ' 【連動修復】預熱時完全使用 _matchCameraId，徹底與設定頁的相機 1（Index 0）綁定
            Dim camId = If(Not String.IsNullOrWhiteSpace(_matchCameraId), _matchCameraId, GetCamId(0))
            If String.IsNullOrWhiteSpace(camId) Then Return

            CameraService.Instance.StartCamera(camId)
            Logger.Info($"[MATCH] 預熱匹配相機: {camId}")
        Catch ex As Exception
            Logger.Error("[MATCH] 預熱相機失敗: " & ex.Message)
        End Try
    End Sub

    Public Sub PlayNoTemplateAlert()
        PlayPromptVoice(VoicePromptNoTemplate)
    End Sub

    ''' <summary>
    ''' 暫停檢測流程：記錄當前階段並暫停，等待信號 2 恢復
    ''' </summary>
    Public Sub PauseDetectionFlow()
        Try
            SyncLock _detectLock
                _isPaused = True
                Logger.Info($"[FLOW] 檢測流程已暫停，當前階段={_flowStage}")
                PlayPromptVoice(VoicePromptDetectionPaused)
            End SyncLock
        Catch ex As Exception
            Logger.Error("[FLOW] 暫停流程失敗: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 恢复检测流程和倒计时
    ''' </summary>
    Public Sub ResumeDetectionFlow()
        Try
            SyncLock _detectLock
                ' 只有在確實暫停的情況下才播報"已恢復"
                If Not _isPaused Then
                    Logger.Info("[FLOW] 檢測流程未暫停，忽略恢復請求")
                    Return
                End If

                _isPaused = False
                Logger.Info("[FLOW] 检测流程已恢复")
                ' 收到信號 2 恢復錄製和檢測時播報
                PlayPromptVoice(VoicePromptStageRecover)
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
End Class
