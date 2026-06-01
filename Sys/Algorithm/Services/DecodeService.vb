Imports System.Drawing
Imports OpenCvSharp
Imports OpenCvSharp.Extensions
Imports ZXing
Imports ZXing.QrCode
Imports ZXing.Windows.Compatibility

Public Class BarcodeDecodeService

    Private ReadOnly _reader As BarcodeReader

    Public Sub New()

        _reader = New BarcodeReader With {
            .AutoRotate = True,
            .TryInverted = True
        }

    End Sub

    Public Function RunRoi(src As Mat, roi As Rect) As String

        If src Is Nothing Then Return ""
        If roi.Width <= 0 OrElse roi.Height <= 0 Then Return ""

        Dim safeRoi = ClampRoi(src, roi)

        Using crop As New Mat(src, safeRoi)

            Using bmp As Bitmap = BitmapConverter.ToBitmap(crop)

                Dim result = _reader.Decode(bmp)

                If result Is Nothing Then Return ""

                Return result.Text

            End Using

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