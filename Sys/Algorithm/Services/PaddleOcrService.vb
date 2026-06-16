Imports OpenCvSharp
Imports Sdcb.PaddleOCR
Imports Sdcb.PaddleInference
Imports Sdcb.PaddleOCR.Models
Imports Sdcb.PaddleOCR.Models.Local

Public Class PaddleOcrService

    Private ReadOnly _ocr As PaddleOcrAll

    Public Sub New()

        Dim model As FullOcrModel = LocalFullModels.ChineseV5

        _ocr = New PaddleOcrAll(model, PaddleDevice.Mkldnn()) With {
            .AllowRotateDetection = False,
            .Enable180Classification = False
        }

    End Sub

    Public Function RunRoi(src As Mat, roi As Rect) As String

        If src Is Nothing Then Return ""
        If roi.Width <= 0 OrElse roi.Height <= 0 Then Return ""

        Dim paddedRoi As New Rect(
        Math.Max(roi.X - 2, 0),
        Math.Max(roi.Y - 2, 0),
        Math.Min(roi.Width + 4, src.Width - roi.X),
        Math.Min(roi.Height + 4, src.Height - roi.Y)
    )

        Using crop As New Mat(src, paddedRoi)

            Dim result = _ocr.Run(crop)

            If result Is Nothing Then Return ""

            Dim msg As String = ""

            For Each r In result.Regions
                msg &= $"文字: {r.Text}  |  置信度: {r.Score:F3}" & vbCrLf
            Next

            MessageBox.Show(msg, "OCR結果")

            Return result.Text

        End Using

    End Function

End Class