Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports OpenCvSharp

Public Class OllamaOcrService
    Private Shared ReadOnly _httpClient As New HttpClient() With {
        .Timeout = TimeSpan.FromSeconds(30)
    }

    ''' <summary>
    ''' 使用 Ollama 呼叫 gml-ocr 模型進行 OCR 辨識
    ''' </summary>
    ''' <param name="src">輸入的 OpenCvSharp Mat 影像</param>
    ''' <param name="roi">要裁切的 ROI 範圍</param>
    ''' <returns>辨識出的文字與假定分數，失敗時返回空字串</returns>
    Public Async Function RunRoiAsync(src As Mat, roi As Rect) As Task(Of OcrResultInfo)
        If src Is Nothing OrElse src.IsDisposed Then
            Return New OcrResultInfo With {.Text = "", .Score = 0}
        End If

        Try
            ' 1. 裁切與轉檔為 PNG 格式
            Dim base64Image As String = ""
            Using crop As Mat = New Mat(src, roi).Clone()
                ' 將 Mat 編碼成 PNG byte 陣列
                Dim buf() As Byte = Nothing
                If Cv2.ImEncode(".png", crop, buf) Then
                    base64Image = Convert.ToBase64String(buf)
                End If
            End Using

            If String.IsNullOrEmpty(base64Image) Then
                Logger.Error("[OllamaOCR] 影像編碼失敗")
                Return New OcrResultInfo With {.Text = "", .Score = 0}
            End If

            ' 2. 建立 Ollama API Request Payload
            ' ollama api: POST /api/generate
            Dim requestPayload As New Dictionary(Of String, Object) From {
                {"model", "glm-ocr:latest"},
                {"prompt", "Please transcribe the text from the image directly. Output ONLY the transcribed text, without any explanation, markdown or introductory words."},
                {"stream", False},
                {"images", New String() {base64Image}}
            }

            Dim jsonContent As String = JsonSerializer.Serialize(requestPayload)
            Dim content As New StringContent(jsonContent, Encoding.UTF8, "application/json")

            ' 3. 發送請求至本地 Ollama (預設連接埠為 11434)
            Dim response = Await _httpClient.PostAsync("http://127.0.0.1:11434/api/generate", content)
            If Not response.IsSuccessStatusCode Then
                Logger.Error($"[OllamaOCR] API 呼叫失敗，HTTP 狀態碼: {response.StatusCode}")
                Return New OcrResultInfo With {.Text = "", .Score = 0}
            End If

            Dim jsonResponse As String = Await response.Content.ReadAsStringAsync()

            ' 4. 解析 Ollama 產生的 JSON 回應
            Using doc As JsonDocument = JsonDocument.Parse(jsonResponse)
                Dim root As JsonElement = doc.RootElement
                If root.TryGetProperty("response", Nothing) Then
                    Dim textResult = root.GetProperty("response").GetString().Trim()
                    ' 去除一些 LLM 習慣性的包裹符號如雙引號等
                    If textResult.StartsWith("""") AndAlso textResult.EndsWith("""") Then
                        textResult = textResult.Substring(1, textResult.Length - 2).Trim()
                    End If

                    Logger.Info($"[OllamaOCR] 識別成功: {textResult}")
                    Return New OcrResultInfo With {
                        .Text = textResult,
                        .Score = 0.95 ' LLM 一般不給像素級置信度，返回高分保底
                    }
                End If
            End Using

        Catch ex As Exception
            Logger.Error($"[OllamaOCR] 異常: {ex.Message}")
        End Try

        Return New OcrResultInfo With {.Text = "", .Score = 0}
    End Function
End Class
