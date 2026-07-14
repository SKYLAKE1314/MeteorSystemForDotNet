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

    Private Async Function WaitBarcodeResultAsync(snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of String)
        Dim decoder = AppRuntime.Barcode
        If decoder Is Nothing Then Return ""

        If snapshot IsNot Nothing AndAlso Not snapshot.EnableBarcode Then
            Logger.Info("[FLOW] 條碼解碼未啟用，直接跳過")
            Return Nothing
        End If

        Dim cameraId = _ocrCameraId
        If String.IsNullOrWhiteSpace(cameraId) Then cameraId = _matchCameraId
        If String.IsNullOrWhiteSpace(cameraId) Then cameraId = GetCamId(0)
        If String.IsNullOrWhiteSpace(cameraId) Then Return ""

        CameraService.Instance.StartCamera(cameraId)

        Dim sw As New Stopwatch()
        sw.Start()

        ' 控制頻率為 100ms 一次（對 4K 相機來說 100ms 是最安全穩定的解碼頻率）
        Const DecodeIntervalMs As Long = 100
        Dim lastAttempt As Long = -DecodeIntervalMs
        Dim decoding As Boolean = False
        Dim resultBox As String = Nothing

        While sw.ElapsedMilliseconds < timeoutMs

            If IsSkipRequested(DetectionFlowStage.Barcode) Then
                Logger.Info("[FLOW] 解碼已跳過")
                Return Nothing
            End If

            If resultBox IsNot Nothing Then
                Logger.Info($"[FLOW] 解碼成功: {resultBox}")
                Return resultBox
            End If

            Dim elapsed = sw.ElapsedMilliseconds
            If Not decoding AndAlso elapsed - lastAttempt >= DecodeIntervalMs Then
                lastAttempt = elapsed
                Dim frame = CameraService.Instance.GetFrame(cameraId)

                If frame IsNot Nothing Then
                    Dim frameCopy = frame
                    Dispatcher.BeginInvoke(Sub() RenderImage.Source = frameCopy)

                    Dim matForDecode As Mat = Nothing
                    Try
                        ' 直接使用高效 extension 轉換
                        matForDecode = frameCopy.ToMat()
                    Catch ex As Exception
                        Logger.Warn("[FLOW] 影像轉 Mat 失敗: " & ex.Message)
                    End Try

                    If matForDecode IsNot Nothing AndAlso Not matForDecode.IsDisposed Then
                        decoding = True
                        Task.Run(Function()
                                     Try
                                         Using mat = matForDecode
                                             ' 1. 全畫面嘗試解碼
                                             Dim text = decoder.Run(mat)

                                             ' 2. 獲取 ROI 範圍
                                             Dim roi = ResolveRoi(snapshot, mat)
                                             Dim isFullFrame = (roi.X = 0 AndAlso roi.Y = 0 AndAlso
                                                            roi.Width = mat.Width AndAlso roi.Height = mat.Height)

                                             If String.IsNullOrWhiteSpace(text) AndAlso Not isFullFrame Then
                                                 text = decoder.RunRoi(mat, roi)
                                             End If

                                             ' 3. 進階處理（【重大修復】：只對小範圍 ROI 進行耗時影像增強！）
                                             If String.IsNullOrWhiteSpace(text) Then
                                                 If Not isFullFrame Then
                                                     ' 有設定 ROI：只針對裁切後的小圖進行高級解碼（速度提升 100 倍！）
                                                     Using roiMat As New Mat(mat, roi)
                                                         text = TryAdvancedBarcodeDecode(decoder, roiMat, snapshot)
                                                     End Using
                                                 Else
                                                     ' 無 ROI 且大於 1080p（4K）：降採樣到 1080p 寬度再處理，防止系統癱瘓
                                                     If mat.Width > 1920 Then
                                                         Dim scale As Double = 1920.0 / mat.Width
                                                         Using downscaledMat = UpscaleImage(mat, scale) ' UpscaleImage 做 Cubic resize
                                                             text = TryAdvancedBarcodeDecode(decoder, downscaledMat, snapshot)
                                                         End Using
                                                     Else
                                                         text = TryAdvancedBarcodeDecode(decoder, mat, snapshot)
                                                     End If
                                                 End If
                                             End If

                                             ' 過濾假陽性
                                             If Not String.IsNullOrWhiteSpace(text) AndAlso Not IsPlausibleBarcodeText(text) Then
                                                 Logger.Warn($"[BARCODE] 解碼結果疑似雜訊已捨棄: '{text}'")
                                                 text = ""
                                             End If

                                             If Not String.IsNullOrWhiteSpace(text) Then
                                                 resultBox = text.Trim()
                                             End If
                                         End Using
                                     Catch ex As Exception
                                         Logger.Warn("[FLOW] 背景解碼異常: " & ex.Message)
                                     Finally
                                         decoding = False
                                     End Try
                                     Return True
                                 End Function)
                    End If
                End If
            End If

            Await Task.Delay(15)
        End While

        Logger.Warn("[FLOW] 解碼超時")
        Return ""
    End Function
    ''' <summary>
    ''' 過濾明顯不合理的解碼結果（例如過短、全同字元的雜訊誤判），
    ''' 降低多角度/增強預處理策略下產生假陽性的機率。
    ''' </summary>
    Private Function IsPlausibleBarcodeText(text As String) As Boolean
        If String.IsNullOrWhiteSpace(text) Then Return False

        Dim trimmed = text.Trim()

        ' 條碼通常至少有效字元數 >= 3
        If trimmed.Length < 3 Then Return False

        ' 全部為相同字元（例如 "111" 或 "---"）視為雜訊
        If trimmed.Distinct().Count() = 1 Then Return False

        Return True
    End Function

    ''' <summary>
    ''' 高級條碼解碼（多執行緒安全版）
    ''' </summary>
    Private Function TryAdvancedBarcodeDecode(decoder As Object, mat As Mat, snapshot As TemplateSnapshot) As String
        Dim tasks As New List(Of Task(Of String))
        Dim cts As New CancellationTokenSource()

        ' 策略 1: CLAHE (對比度增強)
        tasks.Add(Task.Run(Function() As String
                               Return TryDecodeSafe(decoder, mat, AddressOf EnhanceContrast, cts.Token)
                           End Function))

        ' 策略 2: 銳化 (針對失焦模糊)
        tasks.Add(Task.Run(Function() As String
                               Return TryDecodeSafe(decoder, mat, AddressOf SharpenImage, cts.Token)
                           End Function))

        ' 策略 3: 適應性二值化 (針對陰影反光)
        tasks.Add(Task.Run(Function() As String
                               Return TryDecodeSafe(decoder, mat, AddressOf TrueAdaptiveBinarize, cts.Token)
                           End Function))

        ' 策略 4: 多角度
        Dim angles As Double() = {90, 45, -45}
        For Each angle In angles
            Dim currentAngle = angle ' 閉包捕捉
            tasks.Add(Task.Run(Function() As String
                                   Try
                                       If cts.Token.IsCancellationRequested Then Return ""

                                       Using rotated = RotateImage(mat, currentAngle)
                                           If rotated Is Nothing OrElse rotated.IsDisposed Then Return ""
                                           If cts.Token.IsCancellationRequested Then Return ""

                                           Dim result As String = ""
                                           ' 【關鍵防護】排隊進入解碼，保護底層 C++ 不崩潰
                                           SyncLock _decoderLock
                                               result = CStr(decoder.Run(rotated))
                                           End SyncLock

                                           Return If(IsPlausibleBarcodeText(result), result, "")
                                       End Using
                                   Catch ex As Exception
                                       ' 靜默處理單一執行緒錯誤
                                       Return ""
                                   End Try
                               End Function))
        Next

        Try
            ' 等待任何一個任務完成
            While tasks.Count > 0
                Dim completedTask = Task.WhenAny(tasks).Result
                tasks.Remove(completedTask)

                ' 【關鍵防護】如果該任務內部崩潰了，記錄下來並跳過，不要引爆它
                If completedTask.IsFaulted Then
                    Dim errMsg = If(completedTask.Exception?.InnerException?.Message, "未知錯誤")
                    Logger.Warn($"[BARCODE] 單一並行任務失敗 (已忽略): {errMsg}")
                    Continue While
                End If

                ' 走到這裡代表任務正常結束，檢查是否有解出結果
                Dim result = completedTask.Result
                If Not String.IsNullOrEmpty(result) Then
                    cts.Cancel() ' 立刻取消其他還在跑的任務，釋放 CPU 資源
                    Logger.Debug($"[BARCODE] 並行解碼成功，命中結果: {result}")
                    Return result
                End If
            End While
        Catch ex As Exception
            Logger.Warn($"[BARCODE] 總體並行解碼異常: {ex.Message}")
        Finally
            cts.Dispose()
        End Try

        Return ""
    End Function

    ''' <summary>
    ''' 輔助方法：處理影像轉換與安全解碼
    ''' </summary>
    Private Function TryDecodeSafe(decoder As Object, src As Mat, transformFunc As Func(Of Mat, Mat), token As CancellationToken) As String
        Try
            If token.IsCancellationRequested Then Return ""

            Using transformed = transformFunc(src)
                If transformed Is Nothing OrElse transformed.IsDisposed Then Return ""
                If token.IsCancellationRequested Then Return ""

                Dim result As String = ""

                SyncLock _decoderLock
                    result = CStr(decoder.Run(transformed))
                End SyncLock

                Return If(IsPlausibleBarcodeText(result), result, "")
            End Using
        Catch ex As Exception
            ' 單一任務出錯不回報給外層，只回傳空字串
            Return ""
        End Try
    End Function

    ' 輔助方法：統一處理 Mat 的生命週期與委派轉換
    Private Function TryDecodeWithTransform(decoder As Object, src As Mat, transformFunc As Func(Of Mat, Mat), token As CancellationToken) As String
        If token.IsCancellationRequested Then Return ""
        Using transformed = transformFunc(src)
            If transformed Is Nothing Then Return ""
            If token.IsCancellationRequested Then Return ""
            Dim result = decoder.Run(transformed)
            Return If(IsPlausibleBarcodeText(result), result, "")
        End Using
    End Function
    ''' <summary>
    ''' 銳化圖像 (Unsharp Mask) - 針對失焦模糊的條碼特別有效
    ''' </summary>
    Private Function SharpenImage(mat As Mat) As Mat
        Dim blurred As New Mat()
        Dim sharpened As New Mat()
        Try
            ' 使用高斯模糊取得背景平滑影像
            Cv2.GaussianBlur(mat, blurred, New Cv.Size(0, 0), 3)
            ' 原圖放大權重，扣除模糊影像，達到邊緣增強效果
            Cv2.AddWeighted(mat, 1.5, blurred, -0.5, 0, sharpened)
            Return sharpened
        Catch ex As Exception
            Logger.Warn($"[BARCODE] 銳化失敗: {ex.Message}")
            If sharpened IsNot Nothing Then sharpened.Dispose()
            Return Nothing
        Finally
            If blurred IsNot Nothing AndAlso Not blurred.IsDisposed Then blurred.Dispose()
        End Try
    End Function

    ''' <summary>
    ''' 真正的區域適應性二值化 (Local Adaptive Thresholding)
    ''' </summary>
    Private Function TrueAdaptiveBinarize(mat As Mat) As Mat
        Dim gray As Mat = Nothing
        Dim binary As New Mat()
        Try
            If mat.Channels() = 3 Then
                gray = New Mat()
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY)
            Else
                gray = mat.Clone()
            End If

            ' 區塊大小(15)與偏移量(5)可依實際相機解析度微調
            Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 15, 5)
            Return binary
        Catch ex As Exception
            Logger.Warn($"[BARCODE] 適應性二值化失敗: {ex.Message}")
            If binary IsNot Nothing Then binary.Dispose()
            Return Nothing
        Finally
            If gray IsNot Nothing AndAlso Not gray.IsDisposed Then gray.Dispose()
        End Try
    End Function
    ''' <summary>
    ''' 對比度增強（CLAHE - 自適應直方圖均衡化）
    ''' </summary>
    Private Function EnhanceContrast(mat As Mat) As Mat
        Dim gray As Mat = Nothing
        Dim clahe As CLAHE = Nothing
        Dim enhanced As New Mat()
        Try
            If mat.Channels() = 3 Then
                gray = New Mat()
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY)
            Else
                gray = mat.Clone()
            End If

            clahe = Cv2.CreateCLAHE(2.0, New Cv.Size(8, 8))
            clahe.Apply(gray, enhanced)
            Return enhanced
        Catch ex As Exception
            Logger.Warn($"[BARCODE] 對比度增強失敗: {ex.Message}")
            If enhanced IsNot Nothing Then enhanced.Dispose()
            Return Nothing
        Finally
            ' 銷毀 C++ 佔用記憶體
            If gray IsNot Nothing AndAlso Not gray.IsDisposed Then gray.Dispose()
            If clahe IsNot Nothing Then clahe.Dispose()
        End Try
    End Function

    ''' <summary>
    ''' 旋轉圖像
    ''' </summary>
    Private Function RotateImage(mat As Mat, angleDegrees As Double) As Mat
        Try
            Dim center = New Cv.Point2f(mat.Width / 2, mat.Height / 2)
            Dim rotMatrix = Cv2.GetRotationMatrix2D(center, angleDegrees, 1.0)
            Dim rotated = New Mat()
            Cv2.WarpAffine(mat, rotated, rotMatrix, New Cv.Size(mat.Width, mat.Height))
            rotMatrix.Dispose()
            Return rotated
        Catch ex As Exception
            Logger.Warn($"[BARCODE] 旋轉圖像失敗: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 上採樣圖像以提高小目標的可讀性
    ''' </summary>
    Private Function UpscaleImage(mat As Mat, scale As Double) As Mat
        Try
            Dim newSize = New Cv.Size(CInt(mat.Width * scale), CInt(mat.Height * scale))
            Dim upscaled = New Mat()
            Cv2.Resize(mat, upscaled, newSize, 0, 0, InterpolationFlags.Cubic)
            Return upscaled
        Catch ex As Exception
            Logger.Warn($"[BARCODE] 上採樣失敗: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 適應性二值化（Otsu 方法）以處理照明不均的情況
    ''' </summary>
    Private Function AdaptiveBinarize(mat As Mat) As Mat
        Dim gray As Mat = Nothing
        Dim binary As New Mat()
        Try
            If mat.Channels() = 3 Then
                gray = New Mat()
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY)
            Else
                gray = mat.Clone()
            End If

            Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 15, 5)
            Return binary
        Catch ex As Exception
            Logger.Warn($"[BARCODE] 適應性二值化失敗: {ex.Message}")
            If binary IsNot Nothing Then binary.Dispose()
            Return Nothing
        Finally
            If gray IsNot Nothing AndAlso Not gray.IsDisposed Then gray.Dispose()
        End Try
    End Function
End Class
