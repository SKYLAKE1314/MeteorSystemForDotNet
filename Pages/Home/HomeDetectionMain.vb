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

    Private Function ResolveDetectCameraId() As String
        If Not String.IsNullOrWhiteSpace(_detectCameraId) Then
            Return _detectCameraId
        End If

        ' 優先使用第一個相機，然後回退到第二個
        Dim fallback = GetCamId(0)
        If String.IsNullOrWhiteSpace(fallback) Then
            fallback = GetCamId(1)
        End If

        If Not String.IsNullOrWhiteSpace(fallback) Then
            _detectCameraId = fallback
            ' 如果 OCR 相機尚未設置，也使用同一個相機
            If String.IsNullOrWhiteSpace(_ocrCameraId) Then
                _ocrCameraId = fallback
            End If
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
    Public Async Function BtnGetImg_Click() As Task(Of DetectionResult)

        Try
            Dim templatePath = LastTemplateStore.Load()
            If String.IsNullOrWhiteSpace(templatePath) Then Return Nothing

            Logger.Debug($"[DETECT] template={templatePath}")
            Dim templateName = IO.Path.GetFileName(templatePath)

            Dim stage As DetectionFlowStage = DetectionFlowStage.Idle
            If Not TryBeginDetectionSession("MANUAL", stage) Then
                Logger.Info($"[FLOW] 檢測已在進行中：{stage}")
                Return Nothing
            End If

            Dim snapshot = TemplateSnapshotStore.Load()

            ' 過去這裡會用「模板記錄的建模相機」強制覆蓋 _detectCameraId，
            ' 導致使用者在畫面/設定頁選擇的相機被靜默改變（"相機還是不對"的根因之一）。
            ' 現在改為：一律遵循使用者當前選擇的相機，只在不一致時記錄警告，不做任何切換。
            If snapshot IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(snapshot.CameraDeviceId) Then
                If Not String.Equals(snapshot.CameraDeviceId, _detectCameraId, StringComparison.OrdinalIgnoreCase) Then
                    Logger.Warn($"[Camera] 模板建模相機 ({snapshot.CameraDeviceId}) 與當前選定相機 ({_detectCameraId}) 不同，" &
                                "已忽略模板相機設定並沿用使用者當前選擇")
                End If
            End If

            Logger.Debug($"[FLOW] Current stage={stage}, EnableBarcode={If(snapshot IsNot Nothing, snapshot.EnableBarcode, False)}, EnableOcr={If(snapshot IsNot Nothing, snapshot.EnableOcr, False)}")

            ' 匹配多次，在3秒內每秒嘗試一次
            Logger.Info("===== Before Match =====")
            Dim matchResult = Await WaitMultipleMatchAsync(templatePath, snapshot, 3000)
            Logger.Info("===== After Match =====")
            Dim result = matchResult.Result

            ' 即使匹配無結果（無相機畫面）也繼續流程，以NG記錄
            If result Is Nothing Then
                Logger.Warn("[FLOW] 匹配無結果（無相機畫面），以NG繼續流程")
                result = New Draw_opencv.ResultPack With {.Score = 0, .IsOk = False, .Mat = Nothing}
            End If

            If result.Mat IsNot Nothing Then
                Dispatcher.Invoke(Sub()
                                      RenderImage.Source = result.Mat.ToWriteableBitmap()
                                  End Sub)
            End If

            Logger.Info($"Score={result.Score:F3}, OK={result.IsOk}")

            If result.IsOk Then
                _io.HandleOK()
            Else
                _io.HandleNG()
            End If


            Logger.Debug("進入")

            SyncLock _detectLock
                If _activeDetectionResult Is Nothing Then
                    _activeDetectionResult = New DetectionResult With {.List = New List(Of DetectionItem)}
                End If
                If _activeDetectionItem Is Nothing Then
                    _activeDetectionItem = New DetectionItem With {.detectionNo = $"AI-{DateTime.Now:yyyyMMdd-HHmmssfff}"}
                    _activeDetectionResult.List.Add(_activeDetectionItem)
                End If
                _activeDetectionResult.Mat = result.Mat
                _activeDetectionItem.taskPartName = templateName
                _activeDetectionItem.resultType = If(result.IsOk, "MATCH", "MISMATCH")
                _activeDetectionItem.confidence = result.Score
            End SyncLock
            SetFlowStage(DetectionFlowStage.Barcode) ' 重置 skip 標誌，防止上一階段的按鈕操作漏入此階段

            ' 不論OK還是NG，匹配後都播報"匹配完成，請掃描"
            PlayPromptVoice(VoicePromptMatchCompleteScan)
            Logger.Info($"[RESULT] 匹配 - OK={result.IsOk}, Score={result.Score:F3}")
            Logger.Info($"[FLOW] 匹配完成，開始解碼")

            ' 解碼使用與檢測相同的相機（單相機架構），移除舊有寫死切換至相機2的臨時代碼
            ' （該代碼會導致選定相機與實際使用相機不一致，即使用戶已在畫面上選好相機也會被覆蓋）
            If String.IsNullOrWhiteSpace(_ocrCameraId) Then
                _ocrCameraId = _detectCameraId
            End If
            CameraService.Instance.StartCamera(_ocrCameraId)

            Dim code = Await WaitBarcodeResultAsync(snapshot, StageTimeoutMs)
            ' Nothing=跳過  /  ""=超時  /  非空=成功
            Dim barcodeSkipped = (code Is Nothing)
            Dim barcodeTimeout = (Not barcodeSkipped AndAlso String.IsNullOrEmpty(code))

            ' 解碼結果 vs 期望文本（只在成功時比對）
            Dim barcodeExpected = snapshot?.BarcodeExpectedText?.Trim()
            Dim barcodeMatched As Boolean = True
            If Not barcodeSkipped AndAlso Not barcodeTimeout AndAlso
               Not String.IsNullOrWhiteSpace(barcodeExpected) Then
                barcodeMatched = code.Contains(barcodeExpected) OrElse barcodeExpected.Contains(code)
                If Not barcodeMatched Then
                    Logger.Warn($"[RESULT] 條碼不匹配! 期望={barcodeExpected}, 實際={code}")
                End If
            End If

            SyncLock _detectLock
                If _activeDetectionItem IsNot Nothing Then
                    _activeDetectionItem.recognizedPartCode = code
                    If Not barcodeMatched Then _activeDetectionItem.resultType = "MISMATCH"
                End If
            End SyncLock

            ' ← 切換至 OCR 階段（同時清除 Barcode skip 旗標）
            SetFlowStage(DetectionFlowStage.Ocr)

            ' 條碼超時 → 結束流程
            If barcodeTimeout Then
                Dim timeoutOutput As DetectionResult
                SyncLock _detectLock
                    If _activeDetectionItem IsNot Nothing Then
                        _activeDetectionItem.resultType = "MISMATCH"
                    End If
                    timeoutOutput = _activeDetectionResult
                    If timeoutOutput IsNot Nothing Then
                        timeoutOutput.Mat = result.Mat
                        timeoutOutput.IsFinal = True
                        timeoutOutput.Stage = "BARCODE_TIMEOUT"
                    End If
                End SyncLock
                Logger.Warn("[RESULT] 條碼 - 超時")
                Logger.Info("[SUMMARY] ============================")
                Logger.Info($"[SUMMARY] 匹配: OK={result.IsOk} Score={result.Score:F3}")
                Logger.Info("[SUMMARY] 條碼: 超時")
                Logger.Info("[SUMMARY] OCR:  跳過 (無條碼)")
                Logger.Info("[SUMMARY] ============================")
                PlayPromptVoice(VoicePromptStageTimeout)
                PlayPromptVoice(VoicePromptSingleFlowCompleted)
                FinishDetection()
                Return timeoutOutput
            End If

            If barcodeSkipped Then
                Logger.Info("[RESULT] 條碼 - 已跳過")
                PlayPromptVoice(VoicePromptStageSkipped)
            Else
                Logger.Info($"[RESULT] 條碼 - 成功: {code}")
                PlayPromptVoice(VoicePromptCorrect)
            End If

            PlayPromptVoice(VoicePromptDecodeCompleteOcr)
            Logger.Info("[FLOW] 開始 OCR")

            Dim name = Await WaitOcrResultAsync(snapshot, StageTimeoutMs)
            ' Nothing=跳過  /  ""=超時  /  非空=成功
            Dim ocrSkipped = (name Is Nothing)
            Dim ocrTimeout = (Not ocrSkipped AndAlso String.IsNullOrEmpty(name))

            If ocrSkipped Then
                Logger.Info("[RESULT] OCR - 已跳過")
            ElseIf ocrTimeout Then
                Logger.Warn("[RESULT] OCR - 超時")
            Else
                Logger.Info($"[RESULT] OCR - 成功: {name}")
            End If

            Dim finalOutput As DetectionResult
            Dim ocrStage As String = If(ocrSkipped, "OCR_SKIPPED", If(ocrTimeout, "OCR_TIMEOUT", "OCR"))
            SyncLock _detectLock
                If _activeDetectionItem IsNot Nothing Then
                    _activeDetectionItem.recognizedPartName = name
                    If ocrTimeout Then _activeDetectionItem.resultType = "MISMATCH"
                End If
                finalOutput = _activeDetectionResult
                If finalOutput Is Nothing Then
                    finalOutput = New DetectionResult With {.List = New List(Of DetectionItem)}
                    If _activeDetectionItem IsNot Nothing Then
                        finalOutput.List.Add(_activeDetectionItem)
                    End If
                End If
                finalOutput.Mat = result.Mat
                finalOutput.IsFinal = True
                finalOutput.Stage = ocrStage
            End SyncLock

            Logger.Info("[SUMMARY] ============================")
            Logger.Info($"[SUMMARY] 匹配: OK={result.IsOk} Score={result.Score:F3}")
            Logger.Info($"[SUMMARY] 條碼: {If(barcodeSkipped, "跳過", If(barcodeTimeout, "超時", code))}")
            Logger.Info($"[SUMMARY] OCR:  {If(ocrSkipped, "跳過", If(ocrTimeout, "超時", name))}")
            Logger.Info("[SUMMARY] ============================")

            If ocrSkipped Then
                PlayPromptVoice(VoicePromptStageSkipped)
            ElseIf ocrTimeout Then
                PlayPromptVoice(VoicePromptStageTimeout)
            Else
                PlayPromptVoice(VoicePromptCorrect)
            End If
            PlayPromptVoice(VoicePromptSingleFlowCompleted)

            FinishDetection()
            Return finalOutput

        Catch ex As Exception
            Logger.Error("Detection error: " & ex.Message)
            Return Nothing
        End Try

    End Function
End Class
