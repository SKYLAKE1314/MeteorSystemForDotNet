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

    Private Async Function WaitOcrResultAsync(snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of String)
        Dim isAiMode = String.Equals(My.Settings.OcrMode, "AI", StringComparison.OrdinalIgnoreCase)

        If isAiMode Then
            Dim ollamaOcr = AppRuntime.OllamaOCR
            If ollamaOcr Is Nothing Then
                Logger.Error("[OCR] AppRuntime.OllamaOCR 未設定或初始化失敗")
                Return ""
            End If
        Else
            Dim ocr = AppRuntime.OCR
            If ocr Is Nothing Then
                Logger.Error("[OCR] AppRuntime.OCR 未設定或初始化失敗")
                Return ""
            End If
        End If

        Dim cameraId = _ocrCameraId
        If String.IsNullOrWhiteSpace(cameraId) Then cameraId = _matchCameraId
        If String.IsNullOrWhiteSpace(cameraId) Then cameraId = GetCamId(0)

        If String.IsNullOrWhiteSpace(cameraId) Then
            Logger.Error("[OCR] 無法取得有效的 OCR 相機 ID")
            Return ""
        End If

        Logger.Info($"[FLOW] === 進入 OCR 階段 === OCR方式={(If(isAiMode, "AI (Ollama)", "標準 (PaddleOCR)"))}, 選定相機 CamId={cameraId}, 超時設定={timeoutMs}ms")
        CameraService.Instance.StartCamera(cameraId)

        ' 解析期望的 OCR 文本
        Dim expectedTexts As New List(Of String)
        If Not String.IsNullOrWhiteSpace(snapshot?.OcrExpectedText) Then
            expectedTexts.AddRange(snapshot.OcrExpectedText.Split(";"c).
                Select(Function(t) t.Trim()).
                Where(Function(t) Not String.IsNullOrWhiteSpace(t)).
                ToList())
        End If

        Dim angles() As Double = {-45, 0, 15, 45}
        Dim sw As New Stopwatch()
        sw.Start()

        Dim bestText As String = ""
        Dim bestScore As Double = 0
        Dim frameCount As Integer = 0

        ' 【核心優化變數】用於控制 UI 刷新頻率，防止塞爆 Dispatcher 佇列
        Dim lastUiUpdateTime As Long = 0

        While sw.ElapsedMilliseconds < timeoutMs
            If IsSkipRequested(DetectionFlowStage.Ocr) Then
                Logger.Info("[FLOW] OCR 已跳過")
                Return Nothing
            End If

            ' ─── 【⚡ 核心優化 1：相機底層緩存排空機制】 ───
            ' 自動識別「佇列模式」或「快取模式」，瞬間抽乾相機啟動或前段積壓的所有過期舊影格
            Dim frame As BitmapSource = CameraService.Instance.GetFrame(cameraId)
            If frame IsNot Nothing Then
                Dim drainCount As Integer = 0
                While drainCount < 30 ' 最多連續抽排 30 幀歷史積壓，確保不發生無窮迴圈
                    Dim nextFrame As BitmapSource = CameraService.Instance.GetFrame(cameraId)
                    ' 如果拿不到新影格，或者拿到的物件跟當前是同一個（代表驅動是單一快取而非佇列），立刻停止抽排
                    If nextFrame Is Nothing OrElse nextFrame Is frame Then
                        Exit While
                    End If
                    frame = nextFrame
                    drainCount += 1
                End While
                ' Debug 用，上線後可視情況註解
                If drainCount > 0 Then
                    Logger.Debug($"[OCR] 偵測到相機緩存積壓，已自動跳過 {drainCount} 幀過期畫面，直達最新即時影格")
                End If
            End If

            If frame IsNot Nothing Then
                frameCount += 1
                Dim frameCopy = frame

                ' 限制 UI 畫面最高重新整理率
                Dim currentTime = sw.ElapsedMilliseconds
                If currentTime - lastUiUpdateTime >= 33 Then
                    lastUiUpdateTime = currentTime
                    Dispatcher.BeginInvoke(Sub() RenderImage.Source = frameCopy, System.Windows.Threading.DispatcherPriority.Render)
                End If

                Dim result = Await Task.Run(Async Function()
                                                Dim localBestText As String = ""
                                                Dim localBestScore As Double = 0

                                                Using mat = BitmapSourceToMat(frameCopy)
                                                    Dim roi = ResolveRoi(snapshot, mat)

                                                    If roi.Width < 10 OrElse roi.Height < 10 Then
                                                        roi = New OpenCvSharp.Rect(0, 0, mat.Width, mat.Height)
                                                    End If

                                                    If isAiMode Then
                                                        ' AI 方式: 直接呼叫 Ollama 服務
                                                        Dim ocrResult = Await AppRuntime.OllamaOCR.RunRoiAsync(mat, roi)
                                                        If ocrResult IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(ocrResult.Text) Then
                                                            Dim cleanedText = ocrResult.Text.Trim()
                                                            Logger.Info($"[LLMOCR] 即時識別結果: '{cleanedText}' (Score={ocrResult.Score:F3})")

                                                            If expectedTexts.Count > 0 Then
                                                                For Each expected In expectedTexts
                                                                    If cleanedText.Contains(expected) Then
                                                                        Logger.Info($"[FLOW] OllamaOCR 命中模板期望文本: 期望={expected}, 識別={cleanedText}")
                                                                        Return New With {.Text = cleanedText, .Score = ocrResult.Score, .IsMatched = True}
                                                                    End If
                                                                Next
                                                            End If

                                                            localBestScore = ocrResult.Score
                                                            localBestText = cleanedText
                                                        End If
                                                    Else
                                                        ' 標準方式: 旋轉角度多重 OCR
                                                        Using roiMat = New Cv.Mat(mat, roi)
                                                            For Each angle In angles
                                                                Using rotatedRoi = RotateMat(roiMat, angle)
                                                                    Dim fullRoi = New OpenCvSharp.Rect(0, 0, rotatedRoi.Width, rotatedRoi.Height)
                                                                    Dim ocrResult = AppRuntime.OCR.RunRoi(rotatedRoi, fullRoi)

                                                                    If ocrResult IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(ocrResult.Text) Then
                                                                        Dim cleanedText = ocrResult.Text.Trim()
                                                                        Logger.Info($"[OCR] 即時識別結果 (角度 {angle}°): '{cleanedText}' (Score={ocrResult.Score:F3})")

                                                                        ' 只要設定了期望字串，且目前辨識結果包含它，立刻無視置信度直接回傳
                                                                        If expectedTexts.Count > 0 Then
                                                                            For Each expected In expectedTexts
                                                                                If cleanedText.Contains(expected) Then
                                                                                    Logger.Info($"[FLOW] OCR 命中模板期望文本 (無視置信度): 期望={expected}, 識別={cleanedText}")
                                                                                    Return New With {.Text = cleanedText, .Score = ocrResult.Score, .IsMatched = True}
                                                                                End If
                                                                            Next
                                                                        End If

                                                                        ' 若無期望文本，則走原本的「最高分保底」邏輯
                                                                        If ocrResult.Score > localBestScore Then
                                                                            localBestScore = ocrResult.Score
                                                                            localBestText = cleanedText
                                                                        End If
                                                                    End If
                                                                End Using
                                                            Next
                                                        End Using
                                                    End If
                                                End Using

                                                Return New With {.Text = localBestText, .Score = localBestScore, .IsMatched = False}
                                            End Function)

                If result.IsMatched Then
                    Return result.Text
                End If

                If Not String.IsNullOrWhiteSpace(result.Text) Then
                    If result.Score > bestScore Then
                        bestScore = result.Score
                        bestText = result.Text
                    End If

                    ' 無期望文本時的高置信度提前中斷
                    If expectedTexts.Count = 0 AndAlso result.Score >= 0.8 Then
                        Logger.Info($"[FLOW] OCR 高置信度識別成功 (無期望文本): {result.Text} (Score={result.Score:F3})")
                        Return result.Text
                    End If
                End If
            Else
                If frameCount Mod 10 = 0 Then
                    Logger.Warn($"[OCR] 等待相機畫面中... CamId={cameraId}")
                End If
            End If

            ' 適度將等待時間縮短至 20ms-30ms，配合緩存清空，能大幅提高生產線上的採樣即時率
            Await Task.Delay(30)
        End While

        ' 超時後的歷史最高分保底返回
        If Not String.IsNullOrWhiteSpace(bestText) Then
            Logger.Info($"[FLOW] OCR 結束，未完全匹配期望文本，返回歷史最高分結果: {bestText} (Score={bestScore:F3})")
            Return bestText
        End If

        Logger.Warn("[FLOW] OCR 超時，未識別到任何有效文本")
        Return ""
    End Function
End Class
