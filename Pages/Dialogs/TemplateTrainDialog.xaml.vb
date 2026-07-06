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

        ' Camera init is deferred to Loaded (async) to avoid freezing the UI
        AddHandler Me.Loaded, AddressOf TemplateTrainDialog_Loaded
    End Sub

    Private Async Sub TemplateTrainDialog_Loaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler Me.Loaded, AddressOf TemplateTrainDialog_Loaded
        SetLoading(True, "正在初始化相機...")
        Try
            Await Task.Run(Sub()
                               CameraManager.Initialize()
                               CameraManager.Refresh()
                           End Sub)
            Dim cameras = CameraManager.GetCachedCameras()
            If cameras IsNot Nothing AndAlso cameras.Count > 0 Then
                CameraComboBox.ItemsSource = cameras
                CameraComboBox.SelectedIndex = 0
            End If
        Catch ex As Exception
            Logger.Warn("[TrainDialog] 相機初始化失敗: " & ex.Message)
        Finally
            SetLoading(False)
        End Try
    End Sub

    Private Sub SetLoading(visible As Boolean, Optional message As String = "")
        LoadingOverlay.Visibility = If(visible, Visibility.Visible, Visibility.Collapsed)
        If Not String.IsNullOrEmpty(message) Then LoadingText.Text = message
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

    Private Async Sub BtnLoadImage_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim dialog As New Microsoft.Win32.OpenFileDialog With {
                .Filter = "Image|*.png;*.jpg;*.jpeg;*.bmp",
                .Multiselect = False
            }

            If dialog.ShowDialog() <> True Then Return

            LoadImageFromPath(dialog.FileName)
            If _sourceMat IsNot Nothing Then
                Await AutoAnalyzeParamsAsync(_sourceMat)
            End If
        Catch ex As Exception
            MessageBox.Show("載入圖片失敗: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 相機選擇變更事件
    ''' </summary>
    Private Sub CameraComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        ' 簡單的選擇變更處理，實際采集在 BtnCaptureFromCamera_Click 中
    End Sub

    ''' <summary>
    ''' 從相機采集圖像
    ''' </summary>
    Private Async Sub BtnCaptureFromCamera_Click(sender As Object, e As RoutedEventArgs)
        Try
            If CameraComboBox.SelectedValue Is Nothing Then
                MessageBox.Show("請先選擇相機")
                Return
            End If

            Dim cameraId = CameraComboBox.SelectedValue.ToString()
            If String.IsNullOrWhiteSpace(cameraId) Then
                MessageBox.Show("相機設備識別碼無效")
                Return
            End If

            ' 禁用按鈕防止重複點擊
            BtnCaptureFromCamera.IsEnabled = False
            BtnCaptureFromCamera.Content = "⏳ 采集中..."

            ' 啟動相機並等待第一幀
            CameraService.Instance.StartCamera(cameraId)
            Await Task.Delay(100)

            Dim frame = Await WaitForCameraFrameAsync(cameraId, 3000)
            If frame Is Nothing Then
                MessageBox.Show("無法取得相機畫面，請檢查相機是否連接")
                BtnCaptureFromCamera.IsEnabled = True
                BtnCaptureFromCamera.Content = "📷 從相機采集圖像"
                Return
            End If

            ' 將相機幀轉換為 Mat
            Using mat = BitmapSourceConverter.ToMat(frame)
                LoadImageFromMat(mat.Clone())
            End Using

            CameraService.Instance.StopCamera(cameraId)
            If _sourceMat IsNot Nothing Then
                Await AutoAnalyzeParamsAsync(_sourceMat)
            End If
            BtnCaptureFromCamera.IsEnabled = True
            BtnCaptureFromCamera.Content = "📷 從相機采集圖像"

        Catch ex As Exception
            MessageBox.Show("采集圖像失敗: " & ex.Message)
            BtnCaptureFromCamera.IsEnabled = True
            BtnCaptureFromCamera.Content = "📷 從相機采集圖像"
        End Try
    End Sub

    ''' <summary>
    ''' 等待相機幀（超時機制）
    ''' </summary>
    Private Async Function WaitForCameraFrameAsync(cameraId As String, timeoutMs As Integer) As Task(Of BitmapSource)
        Dim sw As New Stopwatch()
        sw.Start()

        While sw.ElapsedMilliseconds < timeoutMs
            Dim frame = CameraService.Instance.GetFrame(cameraId)
            If frame IsNot Nothing Then Return frame
            Await Task.Delay(50)
        End While

        Return Nothing
    End Function

    ''' <summary>
    ''' 從文件路徑加載圖像
    ''' </summary>
    Private Sub LoadImageFromPath(filePath As String)
        Try
            Dim loaded = Cv2.ImRead(filePath)
            If loaded Is Nothing OrElse loaded.Empty() Then
                loaded?.Dispose()
                MessageBox.Show("圖片載入失敗")
                Return
            End If
            LoadImageFromMat(loaded)  ' ownership transfers; LoadImageFromMat sets _sourceMat
        Catch ex As Exception
            MessageBox.Show("載入圖片失敗: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 從 Mat 對象加載圖像到 UI（取得 mat 的所有權）
    ''' </summary>
    Private Sub LoadImageFromMat(mat As Mat)
        Try
            If mat Is Nothing OrElse mat.Empty() Then
                MessageBox.Show("圖像無效")
                Return
            End If

            ' Only dispose the previous mat if it is a different object
            If Not ReferenceEquals(_sourceMat, mat) Then
                _sourceMat?.Dispose()
            End If
            _sourceMat = mat

            ImgSource.Source = BitmapSourceConverter.ToBitmapSource(_sourceMat)
            ClearPolygon()
            ImgPreview.Source = Nothing
            ResetPreviewView()
            TxtPreviewInfo.Text = "完成多邊形後" & vbCrLf & "自動生成"
            TxtRoiStatus.Text = "ROI：左鍵加點，右鍵撤銷，雙擊完成"
            ' AutoAnalyzeParams is called by callers after this returns
        Catch ex As Exception
            MessageBox.Show("處理圖像失敗: " & ex.Message)
        End Try
    End Sub

    Private Async Sub BtnAutoAnalyze_Click(sender As Object, e As RoutedEventArgs)
        If _sourceMat Is Nothing OrElse _sourceMat.Empty() Then
            MessageBox.Show("請先載入圖片")
            Return
        End If
        Await AutoAnalyzeParamsAsync(_sourceMat)
        TxtAutoInfo.Text = "已更新建議參數，可手動調整"
    End Sub

    Private Async Sub BtnRefreshPreview_Click(sender As Object, e As RoutedEventArgs)
        Await UpdatePreviewAsync()
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

    Private Async Sub CompletePolygon()
        If _polygonPoints.Count < 3 Then
            TxtRoiStatus.Text = "ROI：至少需要 3 個點"
            Return
        End If

        _polygonClosed = True
        DrawPolygonOverlay(Nothing)
        TxtRoiStatus.Text = $"ROI：多邊形完成（{_polygonPoints.Count} 點）"
        Await UpdatePreviewAsync()
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

    Private Function ImageToDisplayPoint(p As Point) As WpfPoint
        Dim imgRect = GetImageDisplayRect()
        If imgRect.Width <= 0 OrElse imgRect.Height <= 0 OrElse _sourceMat Is Nothing OrElse _sourceMat.Empty() Then
            Return New WpfPoint(0, 0)
        End If

        Dim rx = p.X / Math.Max(1.0, _sourceMat.Width - 1)
        Dim ry = p.Y / Math.Max(1.0, _sourceMat.Height - 1)

        Return New WpfPoint(
            imgRect.X + rx * imgRect.Width,
            imgRect.Y + ry * imgRect.Height)
    End Function

    Private Async Function LoadTrainingSampleIntoEditor(fileName As String, meta As TemplateTrainingStore.TrainingSampleMeta) As Task
        If String.IsNullOrWhiteSpace(fileName) OrElse meta Is Nothing Then Return

        Try
            Dim mat = TemplateTrainingStore.LoadTrainingSampleImage(_groupPath, fileName)
            If mat Is Nothing OrElse mat.Empty() Then
                MessageBox.Show("訓練樣本圖片載入失敗")
                Return
            End If

            _sourceMat?.Dispose()
            _sourceMat = mat

            ImgSource.Source = BitmapSourceConverter.ToBitmapSource(_sourceMat)
            Me.UpdateLayout()
            RoiOverlay.UpdateLayout()

            PyramidSlider.Value = Math.Max(PyramidSlider.Minimum, Math.Min(PyramidSlider.Maximum, meta.PyramidLevel))
            MatchMethodBox.SelectedIndex = Math.Max(0, Math.Min(2, meta.MatchMethod))
            MinAreaSlider.Value = Math.Max(MinAreaSlider.Minimum, Math.Min(MinAreaSlider.Maximum, meta.MinArea))
            CannyLowSlider.Value = Math.Max(CannyLowSlider.Minimum, Math.Min(CannyLowSlider.Maximum, meta.CannyLow))
            CannyHighSlider.Value = Math.Max(CannyHighSlider.Minimum, Math.Min(CannyHighSlider.Maximum, meta.CannyHigh))
            AngleMinSlider.Value = Math.Max(AngleMinSlider.Minimum, Math.Min(AngleMinSlider.Maximum, meta.AngleMin))
            AngleMaxSlider.Value = Math.Max(AngleMaxSlider.Minimum, Math.Min(AngleMaxSlider.Maximum, meta.AngleMax))
            AngleStepSlider.Value = Math.Max(AngleStepSlider.Minimum, Math.Min(AngleStepSlider.Maximum, meta.AngleStep))

            _polygonPoints.Clear()
            If meta.PolygonPoints IsNot Nothing AndAlso meta.PolygonPoints.Count > 0 Then
                For Each point In meta.PolygonPoints
                    _polygonPoints.Add(ImageToDisplayPoint(New Point(point.X, point.Y)))
                Next
            End If

            _polygonClosed = _polygonPoints.Count >= 3
            DrawPolygonOverlay(Nothing)
            TxtRoiStatus.Text = If(_polygonClosed, $"ROI：已載入 {_polygonPoints.Count} 點", "ROI：樣本無多邊形")
            TxtPreviewInfo.Text = $"已載入樣本{Environment.NewLine}{fileName}"
            If _polygonClosed Then Await UpdatePreviewAsync()
        Catch ex As Exception
            MessageBox.Show("載入訓練樣本失敗: " & ex.Message)
        End Try
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
    ''' Analyze source image and suggest Canny/MinArea/Pyramid parameters (runs OpenCV in background).
    ''' </summary>
    Private Async Function AutoAnalyzeParamsAsync(src As Mat) As Task
        If src Is Nothing OrElse src.Empty() Then Return
        TxtAutoInfo.Text = "分析中..."
        Dim srcClone = src.Clone()
        Try
            Dim lo As Integer, hi As Integer, suggested As Integer, pyramid As Integer
            Await Task.Run(Sub()
                               Using gray As New Mat()
                                   Cv2.CvtColor(srcClone, gray, ColorConversionCodes.BGR2GRAY)
                                   Dim otsuThresh As Double
                                   Using tmp As New Mat()
                                       otsuThresh = Cv2.Threshold(gray, tmp, 0, 255,
                                           ThresholdTypes.Binary Or ThresholdTypes.Otsu)
                                   End Using
                                   lo = CInt(Math.Max(20, Math.Min(120, otsuThresh * 0.5)))
                                   hi = CInt(Math.Max(60, Math.Min(240, otsuThresh)))
                                   If hi <= lo Then hi = lo + 40

                                   suggested = 50
                                   Using edges As New Mat()
                                       Cv2.Canny(gray, edges, lo, hi)
                                       Dim contours As Point()() = Nothing
                                       Cv2.FindContours(edges, contours, Nothing, RetrievalModes.External,
                                                       ContourApproximationModes.ApproxSimple)
                                       If contours IsNot Nothing AndAlso contours.Length > 0 Then
                                           Dim areas = contours.Select(Function(c) Cv2.ContourArea(c)).
                                                        Where(Function(a) a > 1).ToArray()
                                           If areas.Length > 0 Then
                                               Dim medianArea = areas.OrderBy(Function(a) a).ElementAt(areas.Length \ 2)
                                               suggested = CInt(Math.Max(10, Math.Min(500, medianArea * 0.1)))
                                           End If
                                       End If
                                   End Using
                                   Dim shortSide = Math.Min(srcClone.Width, srcClone.Height)
                                   pyramid = If(shortSide >= 800, 3, If(shortSide >= 400, 2, 1))
                               End Using
                           End Sub)
            CannyLowSlider.Value = lo
            CannyHighSlider.Value = hi
            MinAreaSlider.Value = suggested
            PyramidSlider.Value = pyramid
            TxtAutoInfo.Text = $"Canny {lo}/{hi}  MinArea {suggested}  Pyr {pyramid}"
        Catch ex As Exception
            TxtAutoInfo.Text = "自動分析失敗: " & ex.Message
        Finally
            srcClone.Dispose()
        End Try
    End Function

    ''' <summary>
    ''' Render the polygon-masked + edge-overlay preview (OpenCV runs in background).
    ''' </summary>
    Private Async Function UpdatePreviewAsync() As Task
        If _sourceMat Is Nothing OrElse _sourceMat.Empty() Then Return
        If Not _polygonClosed OrElse _polygonPoints.Count < 3 Then Return

        TxtPreviewInfo.Text = "渲染中..."
        Dim srcClone = _sourceMat.Clone()
        Dim lo = CDbl(CannyLowSlider.Value)
        Dim hi = CDbl(CannyHighSlider.Value)
        Dim imagePolygon = _polygonPoints.Select(Function(dp) DisplayToImagePoint(dp)).ToArray()

        Try
            Dim resultBitmap As BitmapSource = Nothing
            Dim infoText As String = ""

            Await Task.Run(Sub()
                               Try
                                   Dim safePoly = imagePolygon.
                                       Select(Function(p) New Point(
                                           Math.Max(0, Math.Min(srcClone.Width - 1, p.X)),
                                           Math.Max(0, Math.Min(srcClone.Height - 1, p.Y)))).
                                       ToArray()

                                   Using mask As Mat = Mat.Zeros(srcClone.Size(), MatType.CV_8UC1)
                                       Cv2.FillPoly(mask, {safePoly}, Scalar.White)

                                       Using gray As New Mat(), edges As New Mat(), maskedEdge As New Mat()
                                           Cv2.CvtColor(srcClone, gray, ColorConversionCodes.BGR2GRAY)
                                           Cv2.Canny(gray, edges, lo, hi)
                                           Cv2.BitwiseAnd(edges, mask, maskedEdge)

                                           Using bg As New Mat(srcClone.Size(), srcClone.Type(), New Scalar(30, 30, 30))
                                               srcClone.CopyTo(bg, mask)
                                               Using edgeColored As New Mat()
                                                   Cv2.CvtColor(maskedEdge, edgeColored, ColorConversionCodes.GRAY2BGR)
                                                   Using cyan As New Mat(srcClone.Size(), srcClone.Type(), New Scalar(200, 180, 0))
                                                       Cv2.BitwiseAnd(edgeColored, cyan, edgeColored)
                                                       Cv2.Add(bg, edgeColored, bg)
                                                   End Using
                                                   resultBitmap = BitmapSourceConverter.ToBitmapSource(bg)
                                                   resultBitmap.Freeze()
                                               End Using
                                               Dim edgePx = Cv2.CountNonZero(maskedEdge)
                                               Dim roiArea = Cv2.ContourArea(safePoly)
                                               Dim density = If(roiArea > 0, edgePx / roiArea, 0)
                                               Dim quality = If(density > 0.15, "✅ 豐富", If(density > 0.05, "🟡 適中", "🔴 稀疏"))
                                               infoText = $"邊緣密度{vbCrLf}{density:F3}{vbCrLf}{quality}"
                                           End Using
                                       End Using
                                   End Using
                               Catch
                                   infoText = "預覽失敗"
                               End Try
                           End Sub)

            If resultBitmap IsNot Nothing Then
                ImgPreview.Source = resultBitmap
                ResetPreviewView()
            End If
            TxtPreviewInfo.Text = infoText
        Finally
            srcClone.Dispose()
        End Try
    End Function

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

    Private Async Sub BtnManageTemplates_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim dlg As New TemplateManageDialog(_groupPath)
            dlg.Owner = Application.Current?.MainWindow
            Dim res = dlg.ShowDialog()
            If res = True AndAlso Not String.IsNullOrWhiteSpace(dlg.SelectedSampleFileName) Then
                Await LoadTrainingSampleIntoEditor(dlg.SelectedSampleFileName, dlg.SelectedSampleMeta)
            End If
            RefreshSampleCount()
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
