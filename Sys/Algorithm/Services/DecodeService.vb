Imports System.Drawing
Imports System.Threading
Imports OpenCvSharp
Imports OpenCvSharp.Extensions
Imports ZXing
Imports ZXing.Common
Imports ZXing.QrCode
Imports ZXing.Windows.Compatibility

Public Class BarcodeDecodeService

    Private ReadOnly _reader As BarcodeReader

    Public Sub New()
        _reader = New BarcodeReader With {
            .AutoRotate = True,
            .TryInverted = True
        }
        ' 允許多種常見條碼格式，TryHarder 提高識別率
        _reader.Options = New DecodingOptions With {
            .TryHarder = True,
            .PossibleFormats = New List(Of BarcodeFormat) From {
                BarcodeFormat.QR_CODE,
                BarcodeFormat.CODE_128,
                BarcodeFormat.CODE_39,
                BarcodeFormat.EAN_13,
                BarcodeFormat.EAN_8,
                BarcodeFormat.DATA_MATRIX,
                BarcodeFormat.PDF_417,
                BarcodeFormat.AZTEC
            }
        }
    End Sub

    Public Function RunRoi(src As Mat, roi As Rect) As String
        If src Is Nothing Then Return ""
        If roi.Width <= 0 OrElse roi.Height <= 0 Then Return ""

        Dim safeRoi = ClampRoi(src, roi)

        Using crop As New Mat(src, safeRoi)
            Dim text = DecodeFromMat(crop)
            If Not String.IsNullOrWhiteSpace(text) Then Return text

            ' CLAHE 增強後再嘗試
            Using gray As New Mat()
                Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY)
                Using clahe = Cv2.CreateCLAHE(2.0, New OpenCvSharp.Size(8, 8))
                    Using enhanced As New Mat()
                        clahe.Apply(gray, enhanced)
                        text = DecodeFromMat(enhanced)
                        If Not String.IsNullOrWhiteSpace(text) Then Return text
                    End Using
                End Using
            End Using

            Return ""
        End Using
    End Function

    ' 全畫面解碼（不限 ROI）：先原圖，再 CLAHE 增強
    Public Function Run(src As Mat) As String
        If src Is Nothing Then Return ""

        ' 原圖
        Dim text = DecodeFromMat(src)
        If Not String.IsNullOrWhiteSpace(text) Then Return text

        ' CLAHE 增強
        Using gray As New Mat()

            If src.Channels() = 3 Then
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY)
            ElseIf src.Channels() = 4 Then
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGRA2GRAY)
            Else
                src.CopyTo(gray)
            End If

            Using clahe = Cv2.CreateCLAHE(2.0, New OpenCvSharp.Size(8, 8))
                Using enhanced As New Mat()
                    clahe.Apply(gray, enhanced)
                    text = DecodeFromMat(enhanced)
                    If Not String.IsNullOrWhiteSpace(text) Then Return text
                End Using
            End Using

        End Using

        Return ""
    End Function

    Private Function DecodeFromMat(m As Mat) As String
        Using bmp As System.Drawing.Bitmap = BitmapConverter.ToBitmap(m)
            Dim result = _reader.Decode(bmp)
            Return If(result IsNot Nothing, result.Text, "")
        End Using
    End Function

    Public Function RunAdvanced(src As Mat) As String
        If src Is Nothing Then Return ""

        ' 1. 全圖直接解
        Dim text = Run(src)
        If Not String.IsNullOrWhiteSpace(text) Then Return text

        ' 2. 高級並行解碼
        If src.Width > 1920 Then
            Dim scale As Double = 1920.0 / src.Width
            Using downscaledMat = ResizeImage(src, scale)
                text = TryAdvancedDecode(downscaledMat)
            End Using
        Else
            text = TryAdvancedDecode(src)
        End If

        Return text
    End Function

    Public Function RunRoiAdvanced(src As Mat, roi As Rect) As String
        If src Is Nothing Then Return ""
        If roi.Width <= 0 OrElse roi.Height <= 0 Then Return ""

        Dim safeRoi = ClampRoi(src, roi)

        ' 1. ROI 直接解 (RunRoi 內部包含原圖與 CLAHE)
        Dim text = RunRoi(src, safeRoi)
        If Not String.IsNullOrWhiteSpace(text) Then Return text

        ' 2. 進階解碼
        Using roiMat As New Mat(src, safeRoi)
            text = TryAdvancedDecode(roiMat)
        End Using

        Return text
    End Function

    Private ReadOnly _readerLock As New Object()

    Private Function TryAdvancedDecode(mat As Mat) As String
        Dim tasks As New List(Of Task(Of String))
        Dim cts As New CancellationTokenSource()

        ' 策略 1: CLAHE (對比度增強)
        tasks.Add(Task.Run(Function() As String
                               Return TryDecodeSafe(mat, AddressOf EnhanceContrast, cts.Token)
                           End Function))

        ' 策略 2: 銳化 (針對失焦模糊)
        tasks.Add(Task.Run(Function() As String
                               Return TryDecodeSafe(mat, AddressOf SharpenImage, cts.Token)
                           End Function))

        ' 策略 3: 適應性二值化 (針對陰影反光)
        tasks.Add(Task.Run(Function() As String
                               Return TryDecodeSafe(mat, AddressOf TrueAdaptiveBinarize, cts.Token)
                           End Function))

        ' 策略 4: 多角度旋轉
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
                                           SyncLock _readerLock
                                               result = DecodeFromMat(rotated)
                                           End SyncLock

                                           Return If(IsPlausibleBarcodeText(result), result, "")
                                       End Using
                                   Catch
                                       Return ""
                                   End Try
                               End Function))
        Next

        Try
            ' 等待任何一個任務完成
            While tasks.Count > 0
                Dim completedTask = Task.WhenAny(tasks).Result
                tasks.Remove(completedTask)

                If completedTask.IsFaulted Then Continue While

                Dim result = completedTask.Result
                If Not String.IsNullOrEmpty(result) Then
                    cts.Cancel() ' 立刻取消其他任務
                    Return result
                End If
            End While
        Catch
            ' 靜默處理
        Finally
            cts.Dispose()
        End Try

        Return ""
    End Function

    Private Function TryDecodeSafe(src As Mat, transformFunc As Func(Of Mat, Mat), token As CancellationToken) As String
        Try
            If token.IsCancellationRequested Then Return ""

            Using transformed = transformFunc(src)
                If transformed Is Nothing OrElse transformed.IsDisposed Then Return ""
                If token.IsCancellationRequested Then Return ""

                Dim result As String = ""
                SyncLock _readerLock
                    result = DecodeFromMat(transformed)
                End SyncLock

                Return If(IsPlausibleBarcodeText(result), result, "")
            End Using
        Catch
            Return ""
        End Try
    End Function

    Private Function IsPlausibleBarcodeText(text As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(text)
    End Function

    Private Function SharpenImage(mat As Mat) As Mat
        Dim blurred As New Mat()
        Dim sharpened As New Mat()
        Try
            Cv2.GaussianBlur(mat, blurred, New OpenCvSharp.Size(0, 0), 3)
            Cv2.AddWeighted(mat, 1.5, blurred, -0.5, 0, sharpened)
            Return sharpened
        Catch
            If sharpened IsNot Nothing Then sharpened.Dispose()
            Return Nothing
        Finally
            If blurred IsNot Nothing AndAlso Not blurred.IsDisposed Then blurred.Dispose()
        End Try
    End Function

    Private Function TrueAdaptiveBinarize(mat As Mat) As Mat
        Dim gray As Mat = Nothing
        Dim binary As New Mat()
        Try
            If mat.Channels() = 3 Then
                gray = New Mat()
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY)
            ElseIf mat.Channels() = 4 Then
                gray = New Mat()
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGRA2GRAY)
            Else
                gray = mat.Clone()
            End If

            Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 15, 5)
            Return binary
        Catch
            If binary IsNot Nothing Then binary.Dispose()
            Return Nothing
        Finally
            If gray IsNot Nothing AndAlso Not gray.IsDisposed Then gray.Dispose()
        End Try
    End Function

    Private Function EnhanceContrast(mat As Mat) As Mat
        Dim gray As Mat = Nothing
        Dim clahe As CLAHE = Nothing
        Dim enhanced As New Mat()
        Try
            If mat.Channels() = 3 Then
                gray = New Mat()
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY)
            ElseIf mat.Channels() = 4 Then
                gray = New Mat()
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGRA2GRAY)
            Else
                gray = mat.Clone()
            End If

            clahe = Cv2.CreateCLAHE(2.0, New OpenCvSharp.Size(8, 8))
            clahe.Apply(gray, enhanced)
            Return enhanced
        Catch
            If enhanced IsNot Nothing Then enhanced.Dispose()
            Return Nothing
        Finally
            If gray IsNot Nothing AndAlso Not gray.IsDisposed Then gray.Dispose()
            If clahe IsNot Nothing Then clahe.Dispose()
        End Try
    End Function

    Private Function RotateImage(mat As Mat, angleDegrees As Double) As Mat
        Try
            Dim center = New Point2f(mat.Width / 2, mat.Height / 2)
            Dim rotMatrix = Cv2.GetRotationMatrix2D(center, angleDegrees, 1.0)
            Dim rotated = New Mat()
            Cv2.WarpAffine(mat, rotated, rotMatrix, New OpenCvSharp.Size(mat.Width, mat.Height))
            rotMatrix.Dispose()
            Return rotated
        Catch
            Return Nothing
        End Try
    End Function

    Private Function ResizeImage(mat As Mat, scale As Double) As Mat
        Try
            Dim newSize = New OpenCvSharp.Size(CInt(mat.Width * scale), CInt(mat.Height * scale))
            Dim resized = New Mat()
            Cv2.Resize(mat, resized, newSize, 0, 0, InterpolationFlags.Cubic)
            Return resized
        Catch
            Return Nothing
        End Try
    End Function

    Private Function ClampRoi(src As Mat, roi As Rect) As Rect
        Dim x = Math.Max(0, Math.Min(roi.X, src.Width - 1))
        Dim y = Math.Max(0, Math.Min(roi.Y, src.Height - 1))

        Dim w = Math.Max(1, Math.Min(roi.Width, src.Width - x))
        Dim h = Math.Max(1, Math.Min(roi.Height, src.Height - y))

        Return New Rect(x, y, w, h)
    End Function

End Class