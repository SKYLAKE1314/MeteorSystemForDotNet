Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports OpenCvSharp

Public Class OllamaOcrService
    ' 【關鍵修復 1】將逾時時間放寬至 3 分鐘。因為模型首次載入至 GPU 顯存需要較長時間，30 秒極易引發連鎖崩潰
    Private Shared ReadOnly _httpClient As New HttpClient() With {
        .Timeout = TimeSpan.FromMinutes(3)
    }

    ''' <summary>
    ''' 在啟動或初始化時，將 glm-ocr 模型完全加載並【永久鎖定】在 GPU 顯存中，實現後續檢測零延遲
    ''' </summary>
    Public Async Function PreloadModelAsync() As Task
        Try
            Logger.Info("[OllamaOCR] 開始預先載入 glm-ocr 模型並鎖定 GPU 顯存...")

            ' 【關鍵修復 2】glm-ocr 是視覺模型，若發送空 Prompt 且無影像會引發 Ollama 底層 500 錯誤。
            ' 這裡送入一張 1x1 像素的黑色隱形 PNG 圖片 Base64，強制觸發視覺編碼器進行完全的顯存初始化。
            Dim dummyImageBase64 As String = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="

            ' 【關鍵修復 3】加入 "keep_alive": -1，這是 Ollama 官方指定的常駐命令，模型載入後將永不釋放
            Dim preloadPayload As New Dictionary(Of String, Object) From {
                {"model", "glm-ocr:latest"},
                {"prompt", "init"},
                {"stream", False},
                {"keep_alive", -1},
                {"images", New String() {dummyImageBase64}}
            }

            Dim jsonContent = JsonSerializer.Serialize(preloadPayload)
            Dim content As New StringContent(jsonContent, Encoding.UTF8, "application/json")

            Dim response = Await _httpClient.PostAsync("http://127.0.0.1:11434/api/generate", content)

            If response.IsSuccessStatusCode Then
                Logger.Info("[OllamaOCR] glm-ocr 模型預先載入成功！已成功常駐 GPU 顯存，後續識別將實現零延遲。")
            Else
                Dim errText = Await response.Content.ReadAsStringAsync()
                Logger.Warn($"[OllamaOCR] 模型預載入失敗，HTTP: {response.StatusCode}, 原因: {errText}")
            End If
        Catch ex As Exception
            Logger.Warn($"[OllamaOCR] 預加載模型失敗（請檢查本地 Ollama 是否正常啟動）: {ex.Message}")
        End Try
    End Function

    ''' <summary>
    ''' 使用 Ollama 呼叫 gml-ocr 模型進行 OCR 辨識
    ''' </summary>
    Public Async Function RunRoiAsync(src As Mat, roi As Rect) As Task(Of OcrResultInfo)
        If src Is Nothing OrElse src.IsDisposed Then
            Return New OcrResultInfo With {.Text = "", .Score = 0}
        End If

        Try
            ' 1. 安全範圍裁切與轉檔為 PNG 格式
            Dim base64Image As String = ""
            Dim safeRoi = roi
            If safeRoi.X < 0 Then safeRoi.X = 0
            If safeRoi.Y < 0 Then safeRoi.Y = 0
            If safeRoi.X + safeRoi.Width > src.Width Then safeRoi.Width = src.Width - safeRoi.X
            If safeRoi.Y + safeRoi.Height > src.Height Then safeRoi.Height = src.Height - safeRoi.Y

            Using crop As New Mat(src, safeRoi)
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
            ' 同樣加入 "keep_alive": -1 雙重保險，確保推論期間模型絕不掉線
            Dim requestPayload As New Dictionary(Of String, Object) From {
                {"model", "glm-ocr:latest"},
                {"prompt", "Please transcribe the text from the image directly. Output ONLY the transcribed text, without any explanation, markdown or introductory words."},
                {"stream", False},
                {"keep_alive", -1},
                {"images", New String() {base64Image}}
            }

            Dim jsonContent As String = JsonSerializer.Serialize(requestPayload)
            Dim content As New StringContent(jsonContent, Encoding.UTF8, "application/json")

            ' 3. 發送請求至本地 Ollama
            Dim response = Await _httpClient.PostAsync("http://127.0.0.1:11434/api/generate", content)
            If Not response.IsSuccessStatusCode Then
                Dim errText = Await response.Content.ReadAsStringAsync()
                Logger.Error($"[OllamaOCR] API 呼叫失敗，HTTP 狀態碼: {response.StatusCode}, 錯誤詳情: {errText}")
                Return New OcrResultInfo With {.Text = "", .Score = 0}
            End If

            Dim jsonResponse As String = Await response.Content.ReadAsStringAsync()

            ' 4. 解析 Ollama 產生的 JSON 回應
            Using doc As JsonDocument = JsonDocument.Parse(jsonResponse)
                Dim root As JsonElement = doc.RootElement
                If root.TryGetProperty("response", Nothing) Then
                    Dim textResult = root.GetProperty("response").GetString().Trim()

                    If textResult.StartsWith("""") AndAlso textResult.EndsWith("""") Then
                        textResult = textResult.Substring(1, textResult.Length - 2).Trim()
                    End If

                    Logger.Info($"[OllamaOCR] 識別成功: {textResult}")
                    Return New OcrResultInfo With {
                        .Text = textResult,
                        .Score = 0.8
                    }
                End If
            End Using

        Catch ex As Exception
            Logger.Error($"[OllamaOCR] 運行異常: {ex.Message}")
        End Try

        Return New OcrResultInfo With {.Text = "", .Score = 0}
    End Function
End Class