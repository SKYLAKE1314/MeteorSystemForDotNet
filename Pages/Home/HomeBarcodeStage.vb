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

        'Dim cameraId = ResolveDetectCameraId()
        '臨時
        Dim cameraId = GetCamId(1)
        '
        If String.IsNullOrWhiteSpace(cameraId) Then Return ""
        ' 臨時
        CameraService.Instance.StopAll()
        Thread.Sleep(100)
        '
        CameraService.Instance.StartCamera(cameraId)

        Dim sw As New Stopwatch()
        sw.Start()

        ' 50ms 一次，最大化解碼頻率
        Const DecodeIntervalMs As Long = 50
        Dim lastAttempt As Long = -DecodeIntervalMs
        Dim decoding As Boolean = False      ' 防止前一幀還未解完就重疊
        Dim resultBox As String = Nothing    ' 跨 Task 傳遞結果

        While sw.ElapsedMilliseconds < timeoutMs

            If IsSkipRequested(DetectionFlowStage.Barcode) Then
                Logger.Info("[FLOW] 解碼已跳過")
                Return Nothing  ' Nothing=跳過 / ""=超時 / 非空=成功
            End If

            ' 有結果立即返回
            If resultBox IsNot Nothing Then
                Logger.Info($"[FLOW] 解碼成功: {resultBox}")
                Return resultBox
            End If

            Dim elapsed = sw.ElapsedMilliseconds
            If Not decoding AndAlso elapsed - lastAttempt >= DecodeIntervalMs Then
                lastAttempt = elapsed
                Dim frame = CameraService.Instance.GetFrame(cameraId)

                If frame IsNot Nothing Then
                    ' 更新預覽（不阻塞）
                    Dim frameCopy = frame
                    Dispatcher.BeginInvoke(Sub() RenderImage.Source = frameCopy)

                    ' 解碼在執行緒池，不 await，用 flag 防重疊
                    decoding = True
                    Task.Run(Function()
                                 Try
                                     Using mat = BitmapSourceToMat(frameCopy)
                                         ' 1. 先全畫面解碼（位置無關）
                                         Dim text = decoder.Run(mat)

                                         ' 2. 全畫面失敗時，若有設定 ROI 再縮小範圍嘗試
                                         If String.IsNullOrWhiteSpace(text) Then
                                             Dim roi = ResolveRoi(snapshot, mat)
                                             Dim isFullFrame = (roi.X = 0 AndAlso roi.Y = 0 AndAlso
                                                                roi.Width = mat.Width AndAlso roi.Height = mat.Height)
                                             If Not isFullFrame Then
                                                 text = decoder.RunRoi(mat, roi)
                                             End If
                                         End If

                                         ' 3. 若仍未解碼，嘗試多角度解碼和增強預處理
                                         If String.IsNullOrWhiteSpace(text) Then
                                             text = TryAdvancedBarcodeDecode(decoder, mat, snapshot)
                                         End If

                                         If Not String.IsNullOrWhiteSpace(text) Then
                                             resultBox = text.Trim()
                                             Logger.Debug($"[BARCODE] 解碼成功: {text}")
                                         End If
                                     End Using
                                 Catch ex As Exception
                                     Logger.Warn("[FLOW] 解碼異常: " & ex.Message)
                                 Finally
                                     decoding = False
                                 End Try
                                 Return True
                             End Function)
                End If
            End If

            Await Task.Delay(10) ' UI 呼吸間隔，不影響解碼頻率
        End While

        Logger.Warn("[FLOW] 解碼超時")
        Return ""
    End Function

    ''' <summary>
    ''' 高級條碼解碼：考慮多角度、小的/模糊的目標
    ''' 策略：1)對比度增強 2)多角度嘗試 3)縮放處理小目標 4)適應性二值化
    ''' </summary>
    Private Function TryAdvancedBarcodeDecode(decoder As Object, mat As Mat, snapshot As TemplateSnapshot) As String
        Try
            ' 策略1：對比度增強（CLAHE）
            Dim enhanced = EnhanceContrast(mat)
            If enhanced IsNot Nothing Then
                Try
                    Dim result = decoder.Run(enhanced)
                    If Not String.IsNullOrWhiteSpace(result) Then
                        Logger.Debug("[BARCODE] 通過對比度增強成功解碼")
                        Return result
                    End If
                Finally
                    enhanced.Dispose()
                End Try
            End If

            ' 策略2：多角度嘗試（±15°, ±30°）
            Dim angles As Integer() = {-30, -15, 15, 30}
            For Each angle In angles
                Dim rotated = RotateImage(mat, angle)
                If rotated IsNot Nothing Then
                    Try
                        Dim result = decoder.Run(rotated)
                        If Not String.IsNullOrWhiteSpace(result) Then
                            Logger.Debug($"[BARCODE] 通過旋轉 {angle}° 成功解碼")
                            Return result
                        End If
                    Finally
                        rotated.Dispose()
                    End Try
                End If
            Next

            ' 策略3：上採樣以提高小目標可讀性（2x 放大）
            Dim upscaled = UpscaleImage(mat, 2.0)
            If upscaled IsNot Nothing Then
                Try
                    Dim result = decoder.Run(upscaled)
                    If Not String.IsNullOrWhiteSpace(result) Then
                        Logger.Debug("[BARCODE] 通過上採樣 (2x) 成功解碼")
                        Return result
                    End If

                    ' 上採樣後也嘗試對比度增強
                    Dim enhancedUpscaled = EnhanceContrast(upscaled)
                    If enhancedUpscaled IsNot Nothing Then
                        Try
                            result = decoder.Run(enhancedUpscaled)
                            If Not String.IsNullOrWhiteSpace(result) Then
                                Logger.Debug("[BARCODE] 通過上採樣+對比度增強成功解碼")
                                Return result
                            End If
                        Finally
                            enhancedUpscaled.Dispose()
                        End Try
                    End If
                Finally
                    upscaled.Dispose()
                End Try
            End If

            ' 策略4：適應性二值化（自動閾值）
            Dim binarized = AdaptiveBinarize(mat)
            If binarized IsNot Nothing Then
                Try
                    Dim result = decoder.Run(binarized)
                    If Not String.IsNullOrWhiteSpace(result) Then
                        Logger.Debug("[BARCODE] 通過適應性二值化成功解碼")
                        Return result
                    End If
                Finally
                    binarized.Dispose()
                End Try
            End If

            ' 都失敗時記錄警告
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
        Try
            Dim gray As Mat
            If mat.Channels() = 3 Then
                gray = New Mat()
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY)
            Else
                gray = mat.Clone()
            End If

            ' CLAHE 參數：clip limit=2.0, tile size=8x8
            Dim clahe = Cv2.CreateCLAHE(2.0, New Cv.Size(8, 8))
            Dim enhanced = New Mat()
            clahe.Apply(gray, enhanced)

            If mat.Channels() = 3 Then
                gray.Dispose()
            End If

            Return enhanced
        Catch ex As Exception
            Logger.Warn($"[BARCODE] 對比度增強失敗: {ex.Message}")
            Return Nothing
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
        Try
            Dim gray As Mat
            If mat.Channels() = 3 Then
                gray = New Mat()
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY)
            Else
                gray = mat.Clone()
            End If

            ' 自適應閾值（Otsu）
            Dim binary = New Mat()
            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary Or ThresholdTypes.Otsu)

            If mat.Channels() = 3 Then
                gray.Dispose()
            End If

            Return binary
        Catch ex As Exception
            Logger.Warn($"[BARCODE] 適應性二值化失敗: {ex.Message}")
            Return Nothing
        End Try
    End Function
End Class
