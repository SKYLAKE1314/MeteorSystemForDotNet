Imports OpenCvSharp
Imports Sdcb.PaddleOCR
Imports Sdcb.PaddleInference
Imports Sdcb.PaddleOCR.Models
Imports Sdcb.PaddleOCR.Models.Local

Public Class PaddleOcrService

    Private ReadOnly _ocr As PaddleOcrAll

    Public Sub New()

        Dim model As FullOcrModel = LocalFullModels.ChineseV3

        _ocr = New PaddleOcrAll(model, PaddleDevice.Mkldnn()) With {
            .AllowRotateDetection = True,
            .Enable180Classification = True
        }

    End Sub

    Public Function RunRoi(src As Mat, roi As Rect) As String

        If src Is Nothing Then Return ""
        If roi.Width <= 0 OrElse roi.Height <= 0 Then Return ""

        Using crop As New Mat(src, roi)

            Using gray As New Mat()
                Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY)

                Dim result = _ocr.Run(gray)

                If result Is Nothing Then Return ""

                Return result.Text
            End Using

        End Using

    End Function

End Class