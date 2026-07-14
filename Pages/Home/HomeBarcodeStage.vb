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
    ''' 高級條碼解碼：考慮多角度、小的/模糊的目標
    ''' 策略：1)對比度增強 2)多角度嘗試 3)縮放處理小目標 4)適應性二值化
    ''' </summary>
    Private Function TryAdvancedBarcodeDecode(decoder As Object, mat As Mat, snapshot As TemplateSnapshot) As String
        Try
            ' 策略1：對比度增強（CLAHE）
            Using enhanced = EnhanceContrast(mat)
                If enhanced IsNot Nothing Then
                    Dim result = decoder.Run(enhanced)
                    If IsPlausibleBarcodeText(result) Then
                        Logger.Debug("[BARCODE] 通過對比度增強成功解碼")
                        Return result
                    End If
                End If
            End Using

            ' 策略2：多角度嘗試
            Dim angles As Integer() = {-30, -15, 15, 30}
            For Each angle In angles
                Using rotated = RotateImage(mat, angle)
                    If rotated IsNot Nothing Then
                        Dim result = decoder.Run(rotated)
                        If IsPlausibleBarcodeText(result) Then
                            Logger.Debug($"[BARCODE] 通過旋轉 {angle}° 成功解碼")
                            Return result
                        End If
                    End If
                End Using
            Next

            ' 策略3：上採樣（【重大修復】只允許對寬度小於 1920 的低畫質影像放大，嚴防 4K 放大到 8K）
            If mat.Width < 1920 Then
                Using upscaled = UpscaleImage(mat, 2.0)
                    If upscaled IsNot Nothing Then
                        Dim result = decoder.Run(upscaled)
                        If IsPlausibleBarcodeText(result) Then
                            Logger.Debug("[BARCODE] 通過上採樣 (2x) 成功解碼")
                            Return result
                        End If

                        ' 上採樣後也嘗試對比度增強
                        Using enhancedUpscaled = EnhanceContrast(upscaled)
                            If enhancedUpscaled IsNot Nothing Then
                                result = decoder.Run(enhancedUpscaled)
                                If IsPlausibleBarcodeText(result) Then
                                    Logger.Debug("[BARCODE] 通過上採樣+對比度增強成功解碼")
                                    Return result
                                End If
                            End If
                        End Using
                    End If
                End Using
            End If

            ' 策略4：適應性二值化
            Using binarized = AdaptiveBinarize(mat)
                If binarized IsNot Nothing Then
                    Dim result = decoder.Run(binarized)
                    If IsPlausibleBarcodeText(result) Then
                        Logger.Debug("[BARCODE] 通過適應性二值化成功解碼")
                        Return result
                    End If
                End If
            End Using

            Logger.Warn("[BARCODE] 高級解碼策略全部失敗")
            Return ""

        Catch ex As Exception
            Logger.Warn($"[BARCODE] 高級解碼異常: {ex.Message}")
            Return ""
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
            ' 徹底銷毀 C++ 佔用記憶體
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

            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary Or ThresholdTypes.Otsu)
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
