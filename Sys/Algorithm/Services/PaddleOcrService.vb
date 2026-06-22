Imports System.Threading
Imports OpenCvSharp
Imports Sdcb.PaddleInference
Imports Sdcb.PaddleOCR
Imports Sdcb.PaddleOCR.Models
Imports Sdcb.PaddleOCR.Models.Local

Public Class PaddleOcrService

    Private ReadOnly _ocr As ThreadLocal(Of PaddleOcrAll)

    Public Sub New()

        Dim model As FullOcrModel = LocalFullModels.ChineseV5

        _ocr = New ThreadLocal(Of PaddleOcrAll)(
            Function()
                Return New PaddleOcrAll(
                    LocalFullModels.ChineseV5,
                    PaddleDevice.OneDnn()) With {
                        .AllowRotateDetection = True,
                        .Enable180Classification = True
                    }
            End Function)
    End Sub

    Public Function RunRoi(
    src As Mat,
    roi As Rect) As OcrResultInfo

        If src Is Nothing Then Return Nothing

        Using crop As Mat = New Mat(src, roi).Clone()

            Dim result = _ocr.Value.Run(crop)

            If result Is Nothing Then Return Nothing

            If result.Regions.Count = 0 Then

                Return New OcrResultInfo With {
                .Text = "",
                .Score = 0
            }

            End If

            Dim score =
            result.Regions.
            Average(Function(x) x.Score)

            Return New OcrResultInfo With {
            .Text = result.Text,
            .Score = score
        }

        End Using

    End Function

    ' 旋轉
    Private Function RotateMat(
    src As Mat,
    angle As Double) As Mat

        Dim center As New Point2f(
            src.Width / 2.0F,
            src.Height / 2.0F)

        Dim matrix =
            Cv2.GetRotationMatrix2D(
                center,
                angle,
                1)

        Dim dst As New Mat()

        Cv2.WarpAffine(
            src,
            dst,
            matrix,
            src.Size())

        Return dst


    End Function

    Private Function GetSkewAngle(src As Mat) As Double

        Dim gray As New Mat()
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY)

        Dim bin As New Mat()
        Cv2.Threshold(gray, bin, 0, 255,
                  ThresholdTypes.Binary Or ThresholdTypes.Otsu)

        Dim bestAngle As Double = 0
        Dim bestVariance As Double = Double.MinValue

        For angle As Double = -20 To 20 Step 1

            Using rotated As Mat = RotateMat(bin, angle)

                Dim proj As New Mat()
                Cv2.Reduce(rotated, proj, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32S)

                Dim mean As Scalar
                Dim stddev As Scalar

                Cv2.MeanStdDev(proj, mean, stddev)

                Dim variance As Double = stddev.Val0

                If variance > bestVariance Then
                    bestVariance = variance
                    bestAngle = angle
                End If

            End Using

        Next

        Return bestAngle

    End Function

    Private Function Deskew(src As Mat, angle As Double) As Mat
        If angle > 90 Then angle -= 180
        If angle < -90 Then angle += 180
        Dim center As New Point2f(src.Width / 2.0F, src.Height / 2.0F)

        Dim matrix = Cv2.GetRotationMatrix2D(center, angle, 1.0)

        Dim dst As New Mat()

        Cv2.WarpAffine(
            src,
            dst,
            matrix,
            src.Size(),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            Scalar.White
        )

        Return dst

    End Function

End Class

Public Class OcrResultInfo

    Public Property Text As String

    Public Property Score As Double

End Class