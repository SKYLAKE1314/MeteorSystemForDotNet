Imports System.Threading
Imports OpenCvSharp
Imports Sdcb.PaddleInference
Imports Sdcb.PaddleOCR
Imports Sdcb.PaddleOCR.Models
Imports Sdcb.PaddleOCR.Models.Local

' 【核心修復 1】將 OcrResultInfo 放在全域最外層，徹底解決「未定義」的紅字錯誤！
Public Class OcrResultInfo
    Public Property Text As String = ""
    Public Property Score As Double = 0
End Class

Public Class PaddleOcrService
    Implements IDisposable

    Private _ocr As PaddleOcrAll
    Private ReadOnly _lock As New Object()
    Private _disposed As Boolean = False

    Public Sub New()
        Try
            Dim model As FullOcrModel = LocalFullModels.ChineseV5

            ' 【核心修復 2】Sdcb.PaddleOCR 的 CPU 模式是不需要傳入第二個參數的喔！
            ' 只要省略不傳，預設就是 100% 穩定的普通 CPU 模式，完美解決「Cpu 不是 PaddleDevice 成員」的錯誤！
            _ocr = New PaddleOcrAll(model)

            _ocr.AllowRotateDetection = False
            _ocr.Enable180Classification = False
            Logger.Info("[PaddleOCR] 採用 CPU 穩定模式初始化成功")
        Catch ex As Exception
            Logger.Error($"[PaddleOCR] 初始化失敗: {ex.Message}")
        End Try
    End Sub

    Public Function RunRoi(src As Mat, roi As Rect) As OcrResultInfo
        If src Is Nothing OrElse src.IsDisposed OrElse src.Empty() Then
            Return New OcrResultInfo With {.Text = "", .Score = 0}
        End If

        ' ROI 安全邊界限縮
        Dim safeRoi = roi
        If safeRoi.X < 0 Then safeRoi.X = 0
        If safeRoi.Y < 0 Then safeRoi.Y = 0
        If safeRoi.Width <= 0 Then safeRoi.Width = src.Width
        If safeRoi.Height <= 0 Then safeRoi.Height = src.Height
        If safeRoi.X + safeRoi.Width > src.Width Then safeRoi.Width = src.Width - safeRoi.X
        If safeRoi.Y + safeRoi.Height > src.Height Then safeRoi.Height = src.Height - safeRoi.Y

        If safeRoi.Width < 2 OrElse safeRoi.Height < 2 Then
            Return New OcrResultInfo With {.Text = "", .Score = 0}
        End If

        Try
            Using crop As New Mat(src, safeRoi)
                If crop.Empty() Then Return New OcrResultInfo With {.Text = "", .Score = 0}

                ' 使用 Clone 強制在記憶體中連續對齊，防止 C++ 底層 Status 3 崩潰
                Using continuousCrop = crop.Clone()
                    Dim result As PaddleOcrResult = Nothing
                    SyncLock _lock
                        If Not _disposed AndAlso _ocr IsNot Nothing Then
                            result = _ocr.Run(continuousCrop)
                        End If
                    End SyncLock

                    If result Is Nothing OrElse result.Regions.Count = 0 Then
                        Return New OcrResultInfo With {.Text = "", .Score = 0}
                    End If

                    Dim score = result.Regions.Average(Function(x) x.Score)

                    Return New OcrResultInfo With {
                        .Text = result.Text,
                        .Score = score
                    }
                End Using
            End Using
        Catch ex As Exception
            Logger.Error($"[PaddleOCR] RunRoi 內部發生異常: {ex.Message}")
            Return New OcrResultInfo With {.Text = "", .Score = 0}
        End Try
    End Function

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not _disposed Then
            If disposing Then
                SyncLock _lock
                    _ocr?.Dispose()
                    _ocr = Nothing
                End SyncLock
            End If
            _disposed = True
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

    Private Function RotateMat(src As Mat, angle As Double) As Mat
        If src Is Nothing OrElse src.IsDisposed OrElse src.Empty() Then Return Nothing
        Try
            Dim center As New Point2f(src.Width / 2.0F, src.Height / 2.0F)
            Using matrix = Cv2.GetRotationMatrix2D(center, angle, 1)
                Dim dst As New Mat()
                Cv2.WarpAffine(src, dst, matrix, src.Size())
                Return dst
            End Using
        Catch ex As Exception
            Logger.Error($"[PaddleOCR] RotateMat 失敗: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Function GetSkewAngle(src As Mat) As Double
        If src Is Nothing OrElse src.IsDisposed OrElse src.Empty() Then Return 0
        Try
            Using gray As New Mat()
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY)
                Using bin As New Mat()
                    Cv2.Threshold(gray, bin, 0, 255, ThresholdTypes.Binary Or ThresholdTypes.Otsu)

                    Dim bestAngle As Double = 0
                    Dim bestVariance As Double = Double.MinValue

                    For angle As Double = -20 To 20 Step 1
                        Using rotated As Mat = RotateMat(bin, angle)
                            If rotated IsNot Nothing AndAlso Not rotated.Empty() Then
                                Using proj As New Mat()
                                    Cv2.Reduce(rotated, proj, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32S)
                                    Dim mean As Scalar = Nothing
                                    Dim stddev As Scalar = Nothing
                                    Cv2.MeanStdDev(proj, mean, stddev)

                                    Dim variance As Double = stddev.Val0
                                    If variance > bestVariance Then
                                        bestVariance = variance
                                        bestAngle = angle
                                    End If
                                End Using
                            End If
                        End Using
                    Next
                    Return bestAngle
                End Using
            End Using
        Catch
            Return 0
        End Try
    End Function

    Private Function Deskew(src As Mat, angle As Double) As Mat
        If src Is Nothing OrElse src.IsDisposed OrElse src.Empty() Then Return Nothing
        Try
            Dim correctedAngle = angle
            If correctedAngle > 90 Then correctedAngle -= 180
            If correctedAngle < -90 Then correctedAngle += 180
            Dim center As New Point2f(src.Width / 2.0F, src.Height / 2.0F)

            Using matrix = Cv2.GetRotationMatrix2D(center, correctedAngle, 1.0)
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
            End Using
        Catch
            Return Nothing
        End Try
    End Function
End Class