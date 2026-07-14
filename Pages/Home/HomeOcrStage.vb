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

        Logger.Info($"[FLOW] === 進入【閃電實時全局 OCR】=== 方式={(If(isAiMode, "AI", "標準"))}")
        CameraService.Instance.StartCamera(cameraId)

        ' 解析期望的 OCR 文本
        Dim expectedTexts As New List(Of String)
        If Not String.IsNullOrWhiteSpace(snapshot?.OcrExpectedText) Then
            expectedTexts.AddRange(snapshot.OcrExpectedText.Split(";"c).
                Select(Function(t) t.Trim()).
                Where(Function(t) Not String.IsNullOrWhiteSpace(t)).
                ToList())
        End If

        Dim sw As New Stopwatch()
        sw.Start()

        Dim bestText As String = ""
        Dim bestScore As Double = 0
        Dim lastUiUpdateTime As Long = 0

        ' 宣告預覽彈窗變數
        Dim previewWin As LivePreviewWindow = Nothing

        Try
            ' 1. 在 UI 執行緒建立並顯示無邊距彈窗
            Dispatcher.Invoke(Sub()
                                  previewWin = New LivePreviewWindow()
                                  previewWin.Show()
                              End Sub)

            While sw.ElapsedMilliseconds < timeoutMs
                If IsSkipRequested(DetectionFlowStage.Ocr) Then
                    Logger.Info("[FLOW] OCR 已跳過")
                    Return Nothing
                End If

                ' 【即時排空】瞬間抽乾相機的所有佇列積壓，確保拿到的 100% 是此時此刻最新鮮的畫面
                Dim frame As BitmapSource = CameraService.Instance.GetFrame(cameraId)
                If frame IsNot Nothing Then
                    While True
                        Dim nextFrame As BitmapSource = CameraService.Instance.GetFrame(cameraId)
                        If nextFrame Is Nothing OrElse nextFrame Is frame Then Exit While
                        frame = nextFrame
                    End While
                End If

                If frame IsNot Nothing Then
                    Dim frameCopy = frame

                    ' UI 畫面高頻更新（維持即時預覽，約 30 FPS 限制以防卡頓）
                    Dim currentTime = sw.ElapsedMilliseconds
                    If currentTime - lastUiUpdateTime >= 33 Then
                        lastUiUpdateTime = currentTime
                        Dispatcher.BeginInvoke(Sub() RenderImage.Source = frameCopy, System.Windows.Threading.DispatcherPriority.Render)

                        If previewWin IsNot Nothing Then
                            previewWin.UpdateFrame(frameCopy)
                        End If
                    End If

                    Dim flowResult As OcrFlowResult = Nothing

                    If isAiMode Then
                        ' AI 模式 (Ollama 全局)
                        Dim localBestText As String = ""
                        Dim localBestScore As Double = 0
                        Dim localIsMatched As Boolean = False

                        Using mat = BitmapSourceToMat(frameCopy)
                            Dim globalRoi = New OpenCvSharp.Rect(0, 0, mat.Width, mat.Height)
                            Using enhancedMat = EnhanceOcrImage(mat)
                                Dim ocrResult = Await AppRuntime.OllamaOCR.RunRoiAsync(enhancedMat, globalRoi)
                                If ocrResult IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(ocrResult.Text) Then
                                    Dim cleanedText = ocrResult.Text.Trim()
                                    Logger.Info($"[LLMOCR] 全局即時結果: '{cleanedText}'")

                                    If expectedTexts.Count > 0 Then
                                        For Each expected In expectedTexts
                                            If cleanedText.Contains(expected) Then
                                                localIsMatched = True
                                                Exit For
                                            End If
                                        Next
                                    End If
                                    localBestScore = ocrResult.Score
                                    localBestText = cleanedText
                                End If
                            End Using
                        End Using

                        flowResult = New OcrFlowResult With {.Text = localBestText, .Score = localBestScore, .IsMatched = localIsMatched}
                    Else
                        ' 標準模式 (PaddleOCR 全局 0° 閃電推論)
                        flowResult = Await Task.Run(Of OcrFlowResult)(
                            Function() As OcrFlowResult
                                Using mat = BitmapSourceToMat(frameCopy)
                                    Using enhancedMat = EnhanceOcrImage(mat)
                                        Dim fullRoi = New OpenCvSharp.Rect(0, 0, enhancedMat.Width, enhancedMat.Height)
                                        Dim ocrResult = AppRuntime.OCR.RunRoi(enhancedMat, fullRoi)

                                        If ocrResult IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(ocrResult.Text) Then
                                            Dim cleanedText = ocrResult.Text.Trim()
                                            Logger.Info($"[OCR] 全局即時結果: '{cleanedText}' (Score={ocrResult.Score.ToString("F3")})")

                                            ' 檢查是否命中期望值
                                            Dim isMatched = False
                                            If expectedTexts.Count > 0 Then
                                                For Each expected In expectedTexts
                                                    If cleanedText.Contains(expected) Then
                                                        isMatched = True
                                                        Exit For
                                                    End If
                                                Next
                                            End If

                                            Return New OcrFlowResult With {
                                                .Text = cleanedText,
                                                .Score = ocrResult.Score,
                                                .IsMatched = isMatched
                                            }
                                        End If
                                    End Using
                                End Using

                                Return New OcrFlowResult With {.Text = "", .Score = 0, .IsMatched = False}
                            End Function)
                    End If

                    If flowResult.IsMatched Then
                        Logger.Info($"[FLOW] 全局 OCR 完美命中期望文本，即時結束流程！")
                        Return flowResult.Text
                    End If

                    If Not String.IsNullOrWhiteSpace(flowResult.Text) Then
                        If flowResult.Score > bestScore Then
                            bestScore = flowResult.Score
                            bestText = flowResult.Text
                        End If

                        ' 無期望文本時，只要有辨識出任何東西就直接回傳，達成真正的實時
                        If expectedTexts.Count = 0 Then
                            Return flowResult.Text
                        End If
                    End If
                End If

                Await Task.Delay(10)
            End While

            If Not String.IsNullOrWhiteSpace(bestText) Then Return bestText
            Logger.Warn("[FLOW] 全局 OCR 超時，未識別到任何有效文本")
            Return ""

        Finally
            ' 流程跑完後，哪怕是強制中斷，蘇蘇都會把它清理乾淨的~
            If previewWin IsNot Nothing Then
                Dispatcher.Invoke(Sub()
                                      Try
                                          previewWin.Close()
                                      Catch
                                      End Try
                                  End Sub)
            End If
        End Try
    End Function

    ''' <summary>
    ''' 【影像增強器】宣告在同一個 Partial Class 內，徹底消除保護層級與未宣告錯誤！
    ''' </summary>
    Private Function EnhanceOcrImage(src As Mat) As Mat
        If src Is Nothing OrElse src.IsDisposed OrElse src.Empty() Then Return src
        Try
            Dim gray As New Mat()
            If src.Channels() = 3 Then
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY)
            Else
                gray = src.Clone()
            End If

            ' 1. 使用 CLAHE 進行局部對比度拉伸（去除反光和暗影）
            Dim enhanced = New Mat()
            Using clahe = Cv2.CreateCLAHE(3.0, New OpenCvSharp.Size(8, 8))
                clahe.Apply(gray, enhanced)
            End Using
            gray.Dispose()

            ' 2. 微量銳利化（提升文字邊緣清晰度）
            Dim sharpened = New Mat()
            Cv2.GaussianBlur(enhanced, sharpened, New OpenCvSharp.Size(0, 0), 3)
            Cv2.AddWeighted(enhanced, 1.5, sharpened, -0.5, 0, sharpened)
            enhanced.Dispose()

            ' 3. 轉回 BGR 格式
            Dim result As New Mat()
            Cv2.CvtColor(sharpened, result, ColorConversionCodes.GRAY2BGR)
            sharpened.Dispose()

            Return result
        Catch ex As Exception
            Return src.Clone()
        End Try
    End Function

End Class

Public Class OcrFlowResult
    Public Property Text As String = ""
    Public Property Score As Double = 0
    Public Property IsMatched As Boolean = False
End Class
