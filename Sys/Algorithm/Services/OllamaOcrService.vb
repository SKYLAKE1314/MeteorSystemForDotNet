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
    ''' 在啟動或初始化時預先載入 (Load) Ollama 的 glm-ocr 模型到 GPU/記憶體中，防止首次檢測時發生嚴重延遲
    ''' </summary>
    Public Async Function PreloadModelAsync() As Task
        Try
            Logger.Info("[OllamaOCR] 開始預先載入 glm-ocr 模型...")

            ' 在 Ollama 中，可以透過只提供模型名稱、空 Prompt 且不帶圖像的方式發送 /api/generate
            ' 這會驅動 Ollama 將指定模型加載到 GPU/VRAM 記憶體中。
            Dim preloadPayload As New Dictionary(Of String, Object) From {
                {"model", "gml-ocr"},
                {"prompt", ""},
                {"stream", False}
            }

            Dim jsonContent = JsonSerializer.Serialize(preloadPayload)
            Dim content As New StringContent(jsonContent, Encoding.UTF8, "application/json")

            Dim response = Await _httpClient.PostAsync("http://127.0.0.1:11434/api/generate", content)
            If response.IsSuccessStatusCode Then
                Logger.Info("[OllamaOCR] glm-ocr 模型預先載入成功！已常駐記憶體。")
            Else
                Logger.Warn($"[OllamaOCR] 模型載入回傳狀態不正常，HTTP: {response.StatusCode}。將在實際檢測時載入。")
            End If
        Catch ex As Exception
            Logger.Warn($"[OllamaOCR] 預加載模型失敗（本機 Ollama 可能未啟動）: {ex.Message}")
        End Try
    End Function

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
                        .Score = 0.8 ' LLM 一般不給像素級置信度，返回高分保底
                    }
                End If
            End Using

        Catch ex As Exception
            Logger.Error($"[OllamaOCR] 異常: {ex.Message}")
        End Try

        Return New OcrResultInfo With {.Text = "", .Score = 0}
    End Function
End Class
