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

        Try
            ' 1. 在 UI 執行緒建立並顯示無邊距彈窗
            Dispatcher.Invoke(Sub()
                                  _activePreviewWin = New LivePreviewWindow()
                                  _activePreviewWin.Show()
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

                    ' UI 畫面高頻更新已由 UpdateFrame/OnFrameArrived 控制，此處不執行同步更新避免 UI 卡頓

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
                                            If IsFuzzyMatch(cleanedText, expected) Then
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
                                                    If IsFuzzyMatch(cleanedText, expected) Then
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
            If _activePreviewWin IsNot Nothing Then
                Dispatcher.Invoke(Sub()
                                      Try
                                          _activePreviewWin.Close()
                                      Catch
                                      End Try
                                      _activePreviewWin = Nothing
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

    ''' <summary>
    ''' Levenshtein 編輯距離算法，計算兩個字串的最小編輯步數（插入、刪除、取代）
    ''' </summary>
    Private Function GetLevenshteinDistance(s As String, t As String) As Integer
        Dim n As Integer = s.Length
        Dim m As Integer = t.Length
        If n = 0 Then Return m
        If m = 0 Then Return n

        Dim d(n, m) As Integer

        For i As Integer = 0 To n
            d(i, 0) = i
        Next
        For j As Integer = 0 To m
            d(0, j) = j
        Next

        For i As Integer = 1 To n
            For j As Integer = 1 To m
                Dim cost As Integer = If(t(j - 1) = s(i - 1), 0, 1)
                d(i, j) = Math.Min(Math.Min(d(i - 1, j) + 1, d(i, j - 1) + 1), d(i - 1, j - 1) + cost)
            Next
        Next

        Return d(n, m)
    End Function

    ''' <summary>
    ''' 滑動視窗模糊匹配，允許預期字元中存在特定長度的字元替換、缺失或多餘字元
    ''' </summary>
    Private Function IsFuzzyMatch(ocrText As String, expected As String) As Boolean
        If String.IsNullOrWhiteSpace(ocrText) OrElse String.IsNullOrWhiteSpace(expected) Then Return False

        Dim cleanOcr = ocrText.Replace(" ", "").Replace("-", "").ToUpper()
        Dim cleanExpected = expected.Replace(" ", "").Replace("-", "").ToUpper()

        ' 1. 精確包含
        If cleanOcr.Contains(cleanExpected) Then Return True

        ' 2. 滑動視窗模糊比對
        Dim lenE = cleanExpected.Length
        If lenE = 0 Then Return False

        Dim minDistance As Integer = Integer.MaxValue
        Dim windowSizes As New List(Of Integer) From {lenE - 1, lenE, lenE + 1}

        For Each wSize In windowSizes
            If wSize < 2 OrElse wSize > cleanOcr.Length Then Continue For
            For i As Integer = 0 To cleanOcr.Length - wSize
                Dim subStr = cleanOcr.Substring(i, wSize)
                Dim dist = GetLevenshteinDistance(subStr, cleanExpected)
                If dist < minDistance Then
                    minDistance = dist
                End If
            Next
        Next

        ' 3. 根據長度判定允許的最大編輯距離門檻
        ' 長度 <= 4: 允許 1 個字元出錯
        ' 長度 <= 8: 允許 2 個字元出錯
        ' 長度 > 8: 允許最大 30% 的字元出錯
        Dim maxAllowedDistance As Integer = 1
        If lenE <= 4 Then
            maxAllowedDistance = 1
        ElseIf lenE <= 8 Then
            maxAllowedDistance = 2
        Else
            maxAllowedDistance = CInt(Math.Floor(lenE * 0.3))
        End If

        Return minDistance <= maxAllowedDistance
    End Function

End Class

Public Class OcrFlowResult
    Public Property Text As String = ""
    Public Property Score As Double = 0
    Public Property IsMatched As Boolean = False
End Class
