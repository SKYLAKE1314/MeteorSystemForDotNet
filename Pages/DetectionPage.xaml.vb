Imports Microsoft.Win32
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions

Public Class DetectionPage

    Private _detector As Yolo26Detector

    Private _modelPath As String

    Private _imagePath As String

    Private Sub AddLog(msg As String)

        Dispatcher.Invoke(Sub()

                              TxtLog.AppendText(
                                  $"[{DateTime.Now:HH:mm:ss}] {msg}" &
                                  Environment.NewLine)

                              TxtLog.ScrollToEnd()

                          End Sub)

    End Sub

    Private Sub BtnLoadModel_Click(
        sender As Object,
        e As RoutedEventArgs)

        Dim dlg As New OpenFileDialog()

        dlg.Filter =
            "ONNX Model|*.onnx"

        If dlg.ShowDialog() <> True Then
            Return
        End If

        _modelPath = dlg.FileName

        TxtModel.Text = _modelPath

        _detector =
            New Yolo26Detector(
                _modelPath)

        AddLog("模型載入完成")

    End Sub

    Private Sub BtnLoadImage_Click(
        sender As Object,
        e As RoutedEventArgs)

        Dim dlg As New OpenFileDialog()

        dlg.Filter =
            "Image|*.jpg;*.png;*.bmp"

        If dlg.ShowDialog() <> True Then
            Return
        End If

        _imagePath = dlg.FileName

        ImgResult.Source =
            New BitmapImage(
                New Uri(_imagePath))

        AddLog("圖片載入完成")

    End Sub

    Private Async Sub BtnInfer_Click(
        sender As Object,
        e As RoutedEventArgs)

        If _detector Is Nothing Then

            AddLog("請先載入模型")
            Return

        End If

        If String.IsNullOrEmpty(
            _imagePath) Then

            AddLog("請先選擇圖片")
            Return

        End If

        Try

            BtnInfer.IsEnabled = False

            _detector.ScoreThreshold =
                CSng(SliderScore.Value)

            Await Task.Run(
                Sub()

                    Dim mat =
                        Cv2.ImRead(
                            _imagePath)

                    Dim result =
                        _detector.Detect(mat)

                    For Each item In result

                        Cv2.Rectangle(
                            mat,
                            New Rect(
                                CInt(item.X),
                                CInt(item.Y),
                                CInt(item.Width),
                                CInt(item.Height)),
                            Scalar.Red,
                            2)

                        Cv2.PutText(
                            mat,
                            $"{item.ClassId}:{item.Score:F2}",
                            New Point(
                                item.X,
                                item.Y - 5),
                            HersheyFonts.HersheySimplex,
                            0.7,
                            Scalar.Lime,
                            2)

                    Next

                    Dispatcher.Invoke(
                        Sub()

                            ImgResult.Source =
                                mat.ToBitmapSource()

                        End Sub)

                    AddLog(
                        $"檢出 {result.Count} 個目標")

                End Sub)

        Catch ex As Exception

            AddLog(ex.Message)

        Finally

            BtnInfer.IsEnabled = True

        End Try

    End Sub

End Class