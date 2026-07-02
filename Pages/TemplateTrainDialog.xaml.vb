Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Imports System.Threading.Tasks
Imports System.Linq
Imports System.Windows.Media
Imports WpfPoint = System.Windows.Point
Imports WpfRect = System.Windows.Rect
Imports WpfPolygon = System.Windows.Shapes.Polygon
Imports WpfPolyline = System.Windows.Shapes.Polyline
Imports WpfEllipse = System.Windows.Shapes.Ellipse

Public Class TemplateTrainDialog

    Private ReadOnly _groupPath As String
    Private _sourceMat As Mat
    Private _polygonPoints As New List(Of WpfPoint)()
    Private _polygonClosed As Boolean = False
    Private _dynamicLine As WpfPolyline
    Private _finalPolygon As WpfPolygon
    Private _masterConfig As TemplateConfig

    Private _previewZoom As Double = 1.0
    Private _previewIsPanning As Boolean = False
    Private _previewLastPanPoint As WpfPoint

    Public Sub New(groupPath As String)
        InitializeComponent()

        _groupPath = TemplateTrainingStore.NormalizeGroupPath(groupPath)
        TxtTemplateName.Text = IO.Path.GetFileName(_groupPath)

        _masterConfig = TemplateManager.LoadGroupBaseConfig(_groupPath)
        ApplyMasterConfig()
        RefreshSampleCount()
    End Sub

    Private Sub ApplyMasterConfig()
        If _masterConfig Is Nothing Then
            TxtMasterThreshold.Text = "母版閾值：0.80 (預設)"
            AngleMinSlider.Value = -60
            AngleMaxSlider.Value = 60
            AngleStepSlider.Value = 3
            Return
        End If

        TxtMasterThreshold.Text = $"母版閾值：{_masterConfig.Threshold:F2}"
        PyramidSlider.Value = Math.Max(PyramidSlider.Minimum, Math.Min(PyramidSlider.Maximum, _masterConfig.PyramidLevel))
        MatchMethodBox.SelectedIndex = Math.Max(0, Math.Min(2, _masterConfig.MatchMethod))
        MinAreaSlider.Value = Math.Max(MinAreaSlider.Minimum, Math.Min(MinAreaSlider.Maximum, _masterConfig.MinArea))
        CannyLowSlider.Value = Math.Max(CannyLowSlider.Minimum, Math.Min(CannyLowSlider.Maximum, _masterConfig.CannyLow))
        CannyHighSlider.Value = Math.Max(CannyHighSlider.Minimum, Math.Min(CannyHighSlider.Maximum, _masterConfig.CannyHigh))
        Dim compatibleMin = Math.Min(_masterConfig.AngleMin, -60)
        Dim compatibleMax = Math.Max(_masterConfig.AngleMax, 60)
        Dim compatibleStep = If(_masterConfig.AngleStep > 0, Math.Min(_masterConfig.AngleStep, 3), 3)
        AngleMinSlider.Value = Math.Max(AngleMinSlider.Minimum, Math.Min(AngleMinSlider.Maximum, compatibleMin))
        AngleMaxSlider.Value = Math.Max(AngleMaxSlider.Minimum, Math.Min(AngleMaxSlider.Maximum, compatibleMax))
        AngleStepSlider.Value = Math.Max(AngleStepSlider.Minimum, Math.Min(AngleStepSlider.Maximum, compatibleStep))
    End Sub

    Private Sub BtnLoadImage_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim dialog As New Microsoft.Win32.OpenFileDialog With {
                .Filter = "Image|*.png;*.jpg;*.jpeg;*.bmp",
                .Multiselect = False
            }

            If dialog.ShowDialog() <> True Then Return

            _sourceMat?.Dispose()
            _sourceMat = Cv2.ImRead(dialog.FileName)

            If _sourceMat Is Nothing OrElse _sourceMat.Empty() Then
                MessageBox.Show("圖片載入失敗")
                Return
            End If

            ImgSource.Source = BitmapSourceConverter.ToBitmapSource(_sourceMat)
            ClearPolygon()
            ImgPreview.Source = Nothing
            ResetPreviewView()
            TxtPreviewInfo.Text = "完成多邊形後\n自動生成"
            TxtRoiStatus.Text = "ROI：左鍵加點，右鍵撤銷，雙擊完成"
            AutoAnalyzeParams(_sourceMat)
        Catch ex As Exception
            MessageBox.Show("載入圖片失敗: " & ex.Message)
        End Try
    End Sub

    Private Sub BtnAutoAnalyze_Click(sender As Object, e As RoutedEventArgs)
        If _sourceMat Is Nothing OrElse _sourceMat.Empty() Then
            MessageBox.Show("請先載入圖片")
            Return
        End If
        AutoAnalyzeParams(_sourceMat)
        TxtAutoInfo.Text = "已更新建議參數，可手動調整"
    End Sub

    Private Sub BtnRefreshPreview_Click(sender As Object, e As RoutedEventArgs)
        UpdatePreview()
    End Sub

    Private Sub BtnClearPolygon_Click(sender As Object, e As RoutedEventArgs)
        ClearPolygon()
        ImgPreview.Source = Nothing
        ResetPreviewView()
        TxtPreviewInfo.Text = "完成多邊形後\n自動生成"
        TxtRoiStatus.Text = "ROI：已清空"
    End Sub

    Private Sub RoiOverlay_MouseLeftButtonDown(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        If _sourceMat Is Nothing Then Return
        Dim p = e.GetPosition(RoiOverlay)

        If e.ClickCount >= 2 Then
            ' Double-click: complete polygon without adding extra point
            If Not _polygonClosed Then
                CompletePolygon()
            End If
            e.Handled = True
            Return
        End If

        ' Single click
        If _polygonClosed Then
            ClearPolygon()
        End If

        _polygonPoints.Add(p)
        DrawPolygonOverlay(currentMouse:=p)
        TxtRoiStatus.Text = $"ROI：已選 {_polygonPoints.Count} 點（雙擊完成，右鍵撤銷）"
        e.Handled = True
    End Sub

    Private Sub RoiOverlay_MouseDoubleClick(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        If _sourceMat Is Nothing Then Return
        If e.ChangedButton <> System.Windows.Input.MouseButton.Left Then Return
        If _polygonClosed Then Return

        ' Remove the duplicate point added by the second single-click of the double-click sequence
        If _polygonPoints.Count > 0 Then
            _polygonPoints.RemoveAt(_polygonPoints.Count - 1)
        End If

        CompletePolygon()
        e.Handled = True
    End Sub

    Private Sub RoiOverlay_MouseMove(sender As Object, e As System.Windows.Input.MouseEventArgs)
        If _sourceMat Is Nothing Then Return
        If _polygonClosed Then Return
        If _polygonPoints.Count = 0 Then Return

        Dim p = e.GetPosition(RoiOverlay)
        DrawPolygonOverlay(currentMouse:=p)
    End Sub

    Private Sub RoiOverlay_MouseRightButtonDown(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        If _sourceMat Is Nothing Then Return
        If _polygonPoints.Count = 0 Then
            TxtRoiStatus.Text = "ROI：目前沒有可撤銷的點"
            Return
        End If

        If _polygonClosed Then
            _polygonClosed = False
        End If

        _polygonPoints.RemoveAt(_polygonPoints.Count - 1)
        DrawPolygonOverlay(Nothing)

        If _polygonPoints.Count = 0 Then
            TxtRoiStatus.Text = "ROI：已清空"
        Else
            TxtRoiStatus.Text = $"ROI：已撤銷，剩餘 {_polygonPoints.Count} 點"
        End If
        e.Handled = True
    End Sub

    Private Sub CompletePolygon()
        If _polygonPoints.Count < 3 Then
            TxtRoiStatus.Text = "ROI：至少需要 3 個點"
            Return
        End If

        _polygonClosed = True
        DrawPolygonOverlay(Nothing)
        TxtRoiStatus.Text = $"ROI：多邊形完成（{_polygonPoints.Count} 點）"
        UpdatePreview()
    End Sub

    Private Sub DrawPolygonOverlay(currentMouse As Nullable(Of WpfPoint))
        RoiOverlay.Children.Clear()

        For Each p In _polygonPoints
            Dim dot As New WpfEllipse With {
                .Width = 8,
                .Height = 8,
                .Fill = Brushes.DeepSkyBlue,
                .IsHitTestVisible = False
            }
            Canvas.SetLeft(dot, p.X - 4)
            Canvas.SetTop(dot, p.Y - 4)
            RoiOverlay.Children.Add(dot)
        Next

        If _polygonPoints.Count >= 2 Then
            Dim line As New WpfPolyline With {
                .Stroke = Brushes.Gold,
                .StrokeThickness = 2,
                .IsHitTestVisible = False
            }
            For Each p In _polygonPoints
                line.Points.Add(p)
            Next
            RoiOverlay.Children.Add(line)
        End If

        If Not _polygonClosed AndAlso currentMouse.HasValue AndAlso _polygonPoints.Count > 0 Then
            _dynamicLine = New WpfPolyline With {
                .Stroke = Brushes.Orange,
                .StrokeThickness = 1.5,
                .StrokeDashArray = New DoubleCollection({4, 3}),
                .IsHitTestVisible = False
            }
            _dynamicLine.Points.Add(_polygonPoints.Last())
            _dynamicLine.Points.Add(currentMouse.Value)
            RoiOverlay.Children.Add(_dynamicLine)
        End If

        If _polygonClosed Then
            _finalPolygon = New WpfPolygon With {
                .Stroke = Brushes.Lime,
                .StrokeThickness = 2,
                .Fill = New SolidColorBrush(Color.FromArgb(70, 80, 200, 120)),
                .IsHitTestVisible = False
            }
            For Each p In _polygonPoints
                _finalPolygon.Points.Add(p)
            Next
            RoiOverlay.Children.Add(_finalPolygon)
        End If
    End Sub

    Private Sub ClearPolygon()
        _polygonPoints.Clear()
        _polygonClosed = False
        RoiOverlay.Children.Clear()
    End Sub

    Private Function GetImageDisplayRect() As WpfRect
        If _sourceMat Is Nothing Then Return New WpfRect()

        Dim controlW = RoiOverlay.ActualWidth
        Dim controlH = RoiOverlay.ActualHeight
        If controlW <= 0 OrElse controlH <= 0 Then Return New WpfRect()

        Dim imgW = CDbl(_sourceMat.Width)
        Dim imgH = CDbl(_sourceMat.Height)
        Dim scale = Math.Min(controlW / imgW, controlH / imgH)

        Dim dispW = imgW * scale
        Dim dispH = imgH * scale
        Dim x = (controlW - dispW) / 2.0
        Dim y = (controlH - dispH) / 2.0

        Return New WpfRect(x, y, dispW, dispH)
    End Function

    Private Function DisplayToImagePoint(p As WpfPoint) As Point
        Dim imgRect = GetImageDisplayRect()
        If imgRect.Width <= 0 OrElse imgRect.Height <= 0 Then Return New Point(0, 0)

        Dim rx = (p.X - imgRect.X) / imgRect.Width
        Dim ry = (p.Y - imgRect.Y) / imgRect.Height

        rx = Math.Max(0, Math.Min(1, rx))
        ry = Math.Max(0, Math.Min(1, ry))

        Dim ix = CInt(Math.Round(rx * (_sourceMat.Width - 1)))
        Dim iy = CInt(Math.Round(ry * (_sourceMat.Height - 1)))

        Return New Point(ix, iy)
    End Function

    Private Async Sub BtnAddSample_Click(sender As Object, e As RoutedEventArgs)
        Try
            If String.IsNullOrWhiteSpace(_groupPath) OrElse Not IO.Directory.Exists(_groupPath) Then
                MessageBox.Show("模板路徑無效")
                Return
            End If

            If _sourceMat Is Nothing OrElse _sourceMat.Empty() Then
                MessageBox.Show("請先載入圖片")
                Return
            End If

            If Not _polygonClosed OrElse _polygonPoints.Count < 3 Then
                MessageBox.Show("請先完成多邊形 ROI（雙擊完成）")
                Return
            End If

            Dim imagePolygon = _polygonPoints.Select(Function(dp) DisplayToImagePoint(dp)).ToList()

            Dim p As New TemplateTrainingStore.TrainingTemplateParams With {
                .MasterThreshold = If(_masterConfig IsNot Nothing, _masterConfig.Threshold, 0.8),
                .PyramidLevel = CInt(PyramidSlider.Value),
                .MatchMethod = MatchMethodBox.SelectedIndex,
                .MinArea = CInt(MinAreaSlider.Value),
                .CannyLow = CInt(CannyLowSlider.Value),
                .CannyHigh = CInt(CannyHighSlider.Value),
                .AngleMin = AngleMinSlider.Value,
                .AngleMax = AngleMaxSlider.Value,
                .AngleStep = AngleStepSlider.Value,
                .MaxSamples = 50
            }

            BtnAddSample.IsEnabled = False

            Dim count = Await Task.Run(Function()
                                           Return TemplateTrainingStore.AddSamplePolygon(_groupPath, _sourceMat, imagePolygon, p)
                                       End Function)

            RefreshSampleCount(count)
            MessageBox.Show("訓練模板已加入")

        Catch ex As Exception
            MessageBox.Show("加入訓練模板失敗: " & ex.Message)
        Finally
            BtnAddSample.IsEnabled = True
        End Try
    End Sub

    Private Sub RefreshSampleCount(Optional count As Integer? = Nothing)
        Dim current = If(count.HasValue, count.Value, TemplateTrainingStore.GetTrainingSampleCount(_groupPath))
        TxtSampleCount.Text = $"樣本數：{current} / 50"
    End Sub

    ''' <summary>
    ''' Analyze source image and suggest Canny/MinArea/Pyramid parameters.
    ''' Uses Otsu threshold as a guide for Canny range, and image resolution for Pyramid.
    ''' </summary>
    Private Sub AutoAnalyzeParams(src As Mat)
        Try
            Using gray As New Mat()
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY)

                ' Otsu thresholding to estimate optimal edge threshold
                Dim otsuThresh As Double
                Using tmp As New Mat()
                    otsuThresh = Cv2.Threshold(gray, tmp, 0, 255,
                        ThresholdTypes.Binary Or ThresholdTypes.Otsu)
                End Using

                ' Canny: low = 0.5x otsu, high = otsu, clamped
                Dim lo = CInt(Math.Max(20, Math.Min(120, otsuThresh * 0.5)))
                Dim hi = CInt(Math.Max(60, Math.Min(240, otsuThresh)))
                If hi <= lo Then hi = lo + 40

                CannyLowSlider.Value = lo
                CannyHighSlider.Value = hi

                ' Contour area estimation: count edges and estimate average contour
                Using edges As New Mat()
                    Cv2.Canny(gray, edges, lo, hi)
                    Dim contours As Point()() = Nothing
                    Cv2.FindContours(edges, contours, Nothing, RetrievalModes.External,
                                    ContourApproximationModes.ApproxSimple)
                    If contours IsNot Nothing AndAlso contours.Length > 0 Then
                        Dim areas = contours.Select(Function(c) Cv2.ContourArea(c)).Where(Function(a) a > 1).ToArray()
                        If areas.Length > 0 Then
                            Dim medianArea = areas.OrderBy(Function(a) a).ElementAt(areas.Length \ 2)
                            Dim suggested = CInt(Math.Max(10, Math.Min(500, medianArea * 0.1)))
                            MinAreaSlider.Value = suggested
                        End If
                    End If
                End Using

                ' Pyramid level: based on image short side
                Dim shortSide = Math.Min(src.Width, src.Height)
                Dim pyramid = If(shortSide >= 800, 3, If(shortSide >= 400, 2, 1))
                PyramidSlider.Value = pyramid

                TxtAutoInfo.Text = $"Canny {lo}/{hi}  MinArea {CInt(MinAreaSlider.Value)}  Pyr {pyramid}"
            End Using
        Catch ex As Exception
            TxtAutoInfo.Text = "自動分析失敗: " & ex.Message
        End Try
    End Sub

    ''' <summary>
    ''' Render the polygon-masked + edge-overlay preview so user can verify the ROI content.
    ''' </summary>
    Private Sub UpdatePreview()
        If _sourceMat Is Nothing OrElse _sourceMat.Empty() Then Return
        If Not _polygonClosed OrElse _polygonPoints.Count < 3 Then Return

        Try
            Dim imagePolygon = _polygonPoints.Select(Function(dp) DisplayToImagePoint(dp)).ToArray()

            Dim safePoly = imagePolygon.
                Select(Function(p) New Point(
                    Math.Max(0, Math.Min(_sourceMat.Width - 1, p.X)),
                    Math.Max(0, Math.Min(_sourceMat.Height - 1, p.Y)))).
                ToArray()

            ' Build filled mask
            Using mask As Mat = Mat.Zeros(_sourceMat.Size(), MatType.CV_8UC1)
                Cv2.FillPoly(mask, {safePoly}, Scalar.White)

                ' Dark background + ROI content
                Using bg As New Mat(_sourceMat.Size(), _sourceMat.Type(), New Scalar(30, 30, 30))
                    _sourceMat.CopyTo(bg, mask)

                    ' Overlay Canny edges in cyan on top for quick visual check
                    Using gray As New Mat(),
                          edges As New Mat(),
                          edgeColored As New Mat()
                        Cv2.CvtColor(bg, gray, ColorConversionCodes.BGR2GRAY)
                        Cv2.Canny(gray, edges,
                            CDbl(CannyLowSlider.Value),
                            CDbl(CannyHighSlider.Value))
                        Cv2.CvtColor(edges, edgeColored, ColorConversionCodes.GRAY2BGR)
                        ' Tint edges cyan
                        Using cyan As New Mat(_sourceMat.Size(), _sourceMat.Type(), New Scalar(200, 180, 0))
                            Cv2.BitwiseAnd(edgeColored, cyan, edgeColored)
                            Cv2.Add(bg, edgeColored, bg)
                        End Using

                        ImgPreview.Source = BitmapSourceConverter.ToBitmapSource(bg)
                        ResetPreviewView()

                        ' Count edge pixels inside polygon as quality hint
                        Dim edgePx As Integer
                        Using maskedEdge As New Mat()
                            Cv2.BitwiseAnd(edges, mask, maskedEdge)
                            edgePx = Cv2.CountNonZero(maskedEdge)
                        End Using
                        Dim roiArea = Cv2.ContourArea(safePoly)
                        Dim density = If(roiArea > 0, edgePx / roiArea, 0)
                        Dim quality = If(density > 0.15, "✅ 豐富", If(density > 0.05, "🟡 適中", "🔴 稀疏"))
                        TxtPreviewInfo.Text = $"邊緣密度\n{density:F3}\n{quality}"
                    End Using
                End Using
            End Using
        Catch ex As Exception
            TxtPreviewInfo.Text = "預覽失敗"
        End Try
    End Sub

    Private Sub PreviewBorder_MouseWheel(sender As Object, e As System.Windows.Input.MouseWheelEventArgs)
        If ImgPreview.Source Is Nothing Then Return

        Dim zoomFactor As Double = If(e.Delta > 0, 1.1, 0.9)
        _previewZoom *= zoomFactor
        _previewZoom = Math.Max(0.2, Math.Min(8.0, _previewZoom))

        PreviewScale.ScaleX = _previewZoom
        PreviewScale.ScaleY = _previewZoom
        e.Handled = True
    End Sub

    Private Sub PreviewBorder_MouseDown(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        If ImgPreview.Source Is Nothing Then Return
        If e.MiddleButton <> System.Windows.Input.MouseButtonState.Pressed Then Return

        _previewIsPanning = True
        _previewLastPanPoint = e.GetPosition(PreviewBorder)
        PreviewBorder.CaptureMouse()
        e.Handled = True
    End Sub

    Private Sub PreviewBorder_MouseMove(sender As Object, e As System.Windows.Input.MouseEventArgs)
        If Not _previewIsPanning Then Return

        Dim pos = e.GetPosition(PreviewBorder)
        Dim dx = pos.X - _previewLastPanPoint.X
        Dim dy = pos.Y - _previewLastPanPoint.Y

        PreviewTranslate.X += dx
        PreviewTranslate.Y += dy
        _previewLastPanPoint = pos
        e.Handled = True
    End Sub

    Private Sub PreviewBorder_MouseUp(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        If Not _previewIsPanning Then Return

        _previewIsPanning = False
        PreviewBorder.ReleaseMouseCapture()
        e.Handled = True
    End Sub

    Private Sub ResetPreviewView()
        _previewZoom = 1.0
        PreviewScale.ScaleX = 1.0
        PreviewScale.ScaleY = 1.0
        PreviewTranslate.X = 0
        PreviewTranslate.Y = 0
        _previewIsPanning = False
    End Sub

    Private Sub BtnManageTemplates_Click(sender As Object, e As RoutedEventArgs)
        Try
            ' Open template management dialog for the current training group
            Dim dlg As New TemplateManageDialog(_groupPath)
            dlg.Owner = Application.Current?.MainWindow
            Dim res = dlg.ShowDialog()
            If res = True Then
                RefreshSampleCount()
            End If
        Catch ex As Exception
            MessageBox.Show($"管理模板失敗：{ex.Message}")
        End Try
    End Sub

    Private Sub BtnClose_Click(sender As Object, e As RoutedEventArgs)
        Me.Close()
    End Sub

    Protected Overrides Sub OnClosed(e As EventArgs)
        MyBase.OnClosed(e)
        _sourceMat?.Dispose()
    End Sub

End Class
