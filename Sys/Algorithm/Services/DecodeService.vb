Imports System.Drawing
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
            ' 先嘗試原圖
            Dim text = DecodeFromMat(crop)
            If Not String.IsNullOrWhiteSpace(text) Then Return text

            ' CLAHE 增強後再嘗試（改善低對比度條碼）
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

    Private Function DecodeFromMat(m As Mat) As String
        Using bmp As System.Drawing.Bitmap = BitmapConverter.ToBitmap(m)
            Dim result = _reader.Decode(bmp)
            Return If(result IsNot Nothing, result.Text, "")
        End Using
    End Function

    Private Function ClampRoi(src As Mat, roi As Rect) As Rect

        Dim x = Math.Max(0, Math.Min(roi.X, src.Width - 1))
        Dim y = Math.Max(0, Math.Min(roi.Y, src.Height - 1))

        Dim w = Math.Max(1, Math.Min(roi.Width, src.Width - x))
        Dim h = Math.Max(1, Math.Min(roi.Height, src.Height - y))

        Return New Rect(x, y, w, h)

    End Function

End Class