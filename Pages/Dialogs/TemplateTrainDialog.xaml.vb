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
    Private _currentLanguage As String = "zh-TW"
    Private _startedCameraId As String = ""

    Public Sub New(groupPath As String)
        InitializeComponent()

        _groupPath = TemplateTrainingStore.NormalizeGroupPath(groupPath)
        TxtTemplateName.Text = IO.Path.GetFileName(_groupPath)

        _masterConfig = TemplateManager.LoadGroupBaseConfig(_groupPath)
        ApplyMasterConfig()
        RefreshSampleCount()

        RefreshLanguageUI()
        AddHandler LanguageManager.LanguageChanged, AddressOf RefreshLanguageUI

        AddHandler Me.Loaded, AddressOf TemplateTrainDialog_Loaded
    End Sub

    Private Async Sub TemplateTrainDialog_Loaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler Me.Loaded, AddressOf TemplateTrainDialog_Loaded

        SetLoading(True, LanguageManager.T("Train_LoadingCamera"))

        Try
            Await Task.Run(Sub()
                               CameraManager.Initialize()
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
            TxtMasterThreshold.Text = LanguageManager.T("Train_MasterThreshDefault")
            AngleMinSlider.Value = -60
            AngleMaxSlider.Value = 60
            AngleStepSlider.Value = 3
            Return
        End If

        TxtMasterThreshold.Text = $"{LanguageManager.T("Train_MasterThreshLabel")}{_masterConfig.Threshold:F2}"
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
            MeteorMessageBox.Show(LanguageManager.T("Train_ErrLoadFile") & ex.Message)
        End Try
    End Sub

    Private Sub CameraComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
    End Sub

    Private Async Sub BtnCaptureFromCamera_Click(sender As Object, e As RoutedEventArgs)
        Try

            Dim camInfo = TryCast(CameraComboBox.SelectedItem, CameraInfo)
            If camInfo Is Nothing Then
                MeteorMessageBox.Show(LanguageManager.T("Train_SelectCamPrompt"))
                Return
            End If

            Dim cameraId = camInfo.DeviceId
            If String.IsNullOrWhiteSpace(cameraId) Then
                MeteorMessageBox.Show(LanguageManager.T("Train_InvalidCamId"))
                Return
            End If

            BtnCaptureFromCamera.IsEnabled = False
            BtnCaptureFromCamera.Content = "⏳ " & LanguageManager.T("Train_Capturing")

            ' 【優先策略】若 HomePage 正在使用此相機，快取已有最新幀，直接取用
            Dim cachedFrame = CameraService.Instance.GetFrame(cameraId)
            If cachedFrame IsNot Nothing Then
                Logger.Info($"[TrainDialog] 直接從快取取得畫面，DeviceId={cameraId}")
                Using mat = ImageConvertHelper.ToMat(cachedFrame)
                    If mat IsNot Nothing AndAlso Not mat.Empty() Then
                        LoadImageFromMat(mat.Clone())
                        If _sourceMat IsNot Nothing Then Await AutoAnalyzeParamsAsync(_sourceMat)
                        BtnCaptureFromCamera.IsEnabled = True
                        BtnCaptureFromCamera.Content = "📷 " & LanguageManager.T("Train_BtnCapture")
                        Return
                    End If
                End Using
            End If

            ' 如果快取無幀且正在檢測中，嚴禁啟動相機搶佔硬體
            If HomePage.IsDetectionRunning Then
                MeteorMessageBox.Show("當前正處於檢測流程中，為避免搶占相機硬體導致檢測黑屏，僅允許擷取正在運行中的相機畫面。若要啟動新相機，請先停止檢測流程。", "流程運行中", MessageBoxButton.OK, MessageBoxImage.Warning)
                BtnCaptureFromCamera.IsEnabled = True
                BtnCaptureFromCamera.Content = "📷 " & LanguageManager.T("Train_BtnCapture")
                Return
            End If

            ' 快取無幀：啟動相機並訂閱事件等待（不再輪詢快取，避免因其他頁面佔用而拿不到）
            Dim wasRunning = CameraService.Instance.IsRunning(cameraId)
            CameraService.Instance.StartCamera(cameraId)
            If Not wasRunning Then
                _startedCameraId = cameraId
            Else
                _startedCameraId = ""
            End If
            
            Dim frame = Await WaitForCameraFrameAsync(cameraId, 5000)
            If frame Is Nothing Then
                MeteorMessageBox.Show(LanguageManager.T("Train_CamTimeoutPrompt"))
                BtnCaptureFromCamera.IsEnabled = True
                BtnCaptureFromCamera.Content = "📷 " & LanguageManager.T("Train_BtnCapture")
                Return
            End If

            Using mat = ImageConvertHelper.ToMat(frame)
                If mat IsNot Nothing AndAlso Not mat.Empty() Then
                    LoadImageFromMat(mat.Clone())
                End If
            End Using

            If _sourceMat IsNot Nothing Then Await AutoAnalyzeParamsAsync(_sourceMat)
            BtnCaptureFromCamera.IsEnabled = True
            BtnCaptureFromCamera.Content = "📷 " & LanguageManager.T("Train_BtnCapture")

        Catch ex As Exception
            MeteorMessageBox.Show(LanguageManager.T("Train_ErrCapture") & ex.Message)
            BtnCaptureFromCamera.IsEnabled = True
            BtnCaptureFromCamera.Content = "📷 " & LanguageManager.T("Train_BtnCapture")
        End Try
    End Sub

    ''' <summary>
    ''' 【修復偶發取圖失敗】訂閱 FrameArrived 事件等待幀，不再輪詢快取。
    ''' 快取輪詢在相機已被其他頁面佔用或剛啟動時無法拿到幀，
    ''' 而事件訂閱能保證第一幀到達時立即返回。
    ''' </summary>
    Private Async Function WaitForCameraFrameAsync(cameraId As String, timeoutMs As Integer) As Task(Of BitmapSource)
        Dim tcs As New TaskCompletionSource(Of BitmapSource)()
        Dim timerHandle As System.Threading.Timer = Nothing

        Dim handler As Action(Of String, BitmapSource) =
            Sub(frameId As String, frameBmp As BitmapSource)
                If Not CameraManager.IsSameDevice(frameId, cameraId) Then Return
                If frameBmp Is Nothing Then Return
                tcs.TrySetResult(frameBmp)
            End Sub

        AddHandler CameraService.Instance.FrameArrived, handler
        Try
            timerHandle = New System.Threading.Timer(
                Sub(state) tcs.TrySetResult(Nothing),
                Nothing, timeoutMs, System.Threading.Timeout.Infinite)

            Return Await tcs.Task
        Finally
            timerHandle?.Dispose()
            RemoveHandler CameraService.Instance.FrameArrived, handler
        End Try
    End Function

    Private Sub LoadImageFromPath(filePath As String)
        Try
            Dim loaded = Cv2.ImRead(filePath)
            If loaded Is Nothing OrElse loaded.Empty() Then
                loaded?.Dispose()
                MeteorMessageBox.Show(LanguageManager.T("Train_ErrImgInvalid"))
                Return
            End If
            LoadImageFromMat(loaded)
        Catch ex As Exception
            MeteorMessageBox.Show(LanguageManager.T("Train_ErrLoadFile") & ex.Message)
        End Try
    End Sub

    Private Sub LoadImageFromMat(mat As Mat)
        Try
            If mat Is Nothing OrElse mat.Empty() Then
                MeteorMessageBox.Show(LanguageManager.T("Train_ErrImgInvalid"))
                Return
            End If

            If Not ReferenceEquals(_sourceMat, mat) Then
                _sourceMat?.Dispose()
            End If
            _sourceMat = mat

            ImgSource.Source = BitmapSourceConverter.ToBitmapSource(_sourceMat)
            ClearPolygon()
            ImgPreview.Source = Nothing
            ResetPreviewView()

            TxtPreviewInfo.Text = LanguageManager.T("Train_PreviewAutoPrompt")
            TxtRoiStatus.Text = LanguageManager.T("Train_RoiStatusDefault")
        Catch ex As Exception
            MeteorMessageBox.Show(LanguageManager.T("Train_ErrProcessMat") & ex.Message)
        End Try
    End Sub

    Private Async Sub BtnAutoAnalyze_Click(sender As Object, e As RoutedEventArgs)
        If _sourceMat Is Nothing OrElse _sourceMat.Empty() Then
            MeteorMessageBox.Show(LanguageManager.T("Train_LoadImgFirst"))
            Return
        End If
        Await AutoAnalyzeParamsAsync(_sourceMat)
        TxtAutoInfo.Text = LanguageManager.T("Train_AutoAnalyzeSuccess")
    End Sub

    Private Async Sub BtnRefreshPreview_Click(sender As Object, e As RoutedEventArgs)
        Await UpdatePreviewAsync()
    End Sub

    Private Sub BtnClearPolygon_Click(sender As Object, e As RoutedEventArgs)
        ClearPolygon()
        ImgPreview.Source = Nothing
        ResetPreviewView()
        TxtPreviewInfo.Text = LanguageManager.T("Train_PreviewAutoPrompt")
        TxtRoiStatus.Text = LanguageManager.T("Train_RoiCleared")
    End Sub

    Private Sub RoiOverlay_MouseLeftButtonDown(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        If _sourceMat Is Nothing Then Return
        Dim p = e.GetPosition(RoiOverlay)

        If e.ClickCount >= 2 Then
            If Not _polygonClosed Then
                CompletePolygon()
            End If
            e.Handled = True
            Return
        End If

        If _polygonClosed Then
            ClearPolygon()
        End If

        _polygonPoints.Add(p)
        DrawPolygonOverlay(currentMouse:=p)
        TxtRoiStatus.Text = $"{LanguageManager.T("Train_RoiSelectedPrefix")} {_polygonPoints.Count} {LanguageManager.T("Train_RoiSelectedSuffix")}"
        e.Handled = True
    End Sub

    Private Sub RoiOverlay_MouseDoubleClick(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        If _sourceMat Is Nothing Then Return
        If e.ChangedButton <> System.Windows.Input.MouseButton.Left Then Return
        If _polygonClosed Then Return

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
        ' 【⚡ 核心修正】把 Threading.Tasks.Task 改回正常的 Nothing，解決類別類型錯誤！
        If _sourceMat Is Nothing Then Return

        If _polygonPoints.Count = 0 Then
            TxtRoiStatus.Text = LanguageManager.T("Train_RoiNoUndo")
            Return
        End If

        If _polygonClosed Then
            _polygonClosed = False
        End If

        _polygonPoints.RemoveAt(_polygonPoints.Count - 1)
        DrawPolygonOverlay(Nothing)

        If _polygonPoints.Count = 0 Then
            TxtRoiStatus.Text = LanguageManager.T("Train_RoiCleared")
        Else
            TxtRoiStatus.Text = $"{LanguageManager.T("Train_RoiUndoLeft")} {_polygonPoints.Count} {LanguageManager.T("Train_RoiUndoRight")}"
        End If
        e.Handled = True
    End Sub

    Private Async Sub CompletePolygon()
        If _polygonPoints.Count < 3 Then
            TxtRoiStatus.Text = LanguageManager.T("Train_RoiMinPoints")
            Return
        End If

        _polygonClosed = True
        DrawPolygonOverlay(Nothing)
        TxtRoiStatus.Text = $"{LanguageManager.T("Train_RoiComplete")}（{_polygonPoints.Count} {LanguageManager.T("Train_PointsUnit")}）"
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
        Return New WpfPoint(imgRect.X + rx * imgRect.Width, imgRect.Y + ry * imgRect.Height)
    End Function

    Private Async Function LoadTrainingSampleIntoEditor(fileName As String, meta As TemplateTrainingStore.TrainingSampleMeta) As Task
        If String.IsNullOrWhiteSpace(fileName) OrElse meta Is Nothing Then Return
        Try
            Dim mat = TemplateTrainingStore.LoadTrainingSampleImage(_groupPath, fileName)
            If mat Is Nothing OrElse mat.Empty() Then
                MeteorMessageBox.Show(LanguageManager.T("Train_ErrLoadSampleImg"))
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
            TxtRoiStatus.Text = If(_polygonClosed, $"{LanguageManager.T("Train_RoiLoadedPrefix")} {_polygonPoints.Count} {LanguageManager.T("Train_PointsUnit")}", LanguageManager.T("Train_RoiNoPolygon"))
            TxtPreviewInfo.Text = $"{LanguageManager.T("Train_SampleLoaded")}{Environment.NewLine}{fileName}"
            If _polygonClosed Then Await UpdatePreviewAsync()
        Catch ex As Exception
            MeteorMessageBox.Show(LanguageManager.T("Train_ErrLoadSample") & ex.Message)
        End Try
    End Function

    Private Async Sub BtnAddSample_Click(sender As Object, e As RoutedEventArgs)
        Try
            If String.IsNullOrWhiteSpace(_groupPath) OrElse Not IO.Directory.Exists(_groupPath) Then
                MeteorMessageBox.Show(LanguageManager.T("Train_InvalidTemplatePath"))
                Return
            End If

            If _sourceMat Is Nothing OrElse _sourceMat.Empty() Then
                MeteorMessageBox.Show(LanguageManager.T("Train_LoadImgFirst"))
                Return
            End If

            If Not _polygonClosed OrElse _polygonPoints.Count < 3 Then
                MeteorMessageBox.Show(LanguageManager.T("Train_CompletePolygonFirst"))
                Return
            End If

            Dim imagePolygon = _polygonPoints.Select(Function(dp)
                                                         Dim resPoint = DisplayToImagePoint(dp)
                                                         Dim safeX = Math.Max(0, Math.Min(resPoint.X, _sourceMat.Width - 1))
                                                         Dim safeY = Math.Max(0, Math.Min(resPoint.Y, _sourceMat.Height - 1))
                                                         Return New Point(safeX, safeY)
                                                     End Function).ToList()

            Dim pts = imagePolygon.Select(Function(pt) New OpenCvSharp.Point(pt.X, pt.Y)).ToArray()
            Dim bbox = Cv2.BoundingRect(pts)

            Dim x1 = Math.Max(0, bbox.X)
            Dim y1 = Math.Max(0, bbox.Y)
            Dim x2 = Math.Min(_sourceMat.Width, bbox.X + bbox.Width)
            Dim y2 = Math.Min(_sourceMat.Height, bbox.Y + bbox.Height)

            Dim cleanRectPolygon As New List(Of Point) From {
                New Point(x1, y1),
                New Point(x2, y1),
                New Point(x2, y2),
                New Point(x1, y2)
            }

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
                                           Return TemplateTrainingStore.AddSamplePolygon(_groupPath, _sourceMat, cleanRectPolygon, p)
                                       End Function)

            RefreshSampleCount(count)
            MeteorMessageBox.Show(LanguageManager.T("Train_SampleAddedSuccess"))

        Catch ex As Exception
            MeteorMessageBox.Show(LanguageManager.T("Train_ErrAddSample") & ex.Message)
        Finally
            BtnAddSample.IsEnabled = True
        End Try
    End Sub

    Private Sub RefreshSampleCount(Optional count As Integer? = Nothing)
        Dim current = If(count.HasValue, count.Value, TemplateTrainingStore.GetTrainingSampleCount(_groupPath))
        TxtSampleCount.Text = $"{LanguageManager.T("Train_SampleCountLabel")} {current} / 50"
    End Sub

    Private Async Function AutoAnalyzeParamsAsync(src As Mat) As Task
        If src Is Nothing OrElse src.Empty() Then Return
        TxtAutoInfo.Text = LanguageManager.T("Train_Analyzing")
        Dim srcClone = src.Clone()
        Try
            Dim lo As Integer, hi As Integer, suggested As Integer, pyramid As Integer
            Await Task.Run(Sub()
                               Using gray As New Mat()
                                   Cv2.CvtColor(srcClone, gray, ColorConversionCodes.BGR2GRAY)
                                   Dim otsuThresh As Double
                                   Using tmp As New Mat()
                                       otsuThresh = Cv2.Threshold(gray, tmp, 0, 255, ThresholdTypes.Binary Or ThresholdTypes.Otsu)
                                   End Using
                                   lo = CInt(Math.Max(20, Math.Min(120, otsuThresh * 0.5)))
                                   hi = CInt(Math.Max(60, Math.Min(240, otsuThresh)))
                                   If hi <= lo Then hi = lo + 40

                                   suggested = 50
                                   Using edges As New Mat()
                                       Cv2.Canny(gray, edges, lo, hi)
                                       Dim contours As Point()() = Nothing
                                       Cv2.FindContours(edges, contours, Nothing, RetrievalModes.External, ContourApproximationModes.ApproxSimple)
                                       If contours IsNot Nothing AndAlso contours.Length > 0 Then
                                           Dim areas = contours.Select(Function(c) Cv2.ContourArea(c)).Where(Function(a) a > 1).ToArray()
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
            TxtAutoInfo.Text = LanguageManager.T("Train_ErrAutoAnalyze") & ex.Message
        Finally
            srcClone.Dispose()
        End Try
    End Function

    Private Async Function UpdatePreviewAsync() As Task
        If _sourceMat Is Nothing OrElse _sourceMat.Empty() Then Return
        If Not _polygonClosed OrElse _polygonPoints.Count < 3 Then Return

        TxtPreviewInfo.Text = LanguageManager.T("Train_Rendering")
        Dim srcClone = _sourceMat.Clone()
        Dim lo = CDbl(CannyLowSlider.Value)
        Dim hi = CDbl(CannyHighSlider.Value)
        Dim imagePolygon = _polygonPoints.Select(Function(dp) DisplayToImagePoint(dp)).ToArray()

        Try
            Dim resultBitmap As BitmapSource = Nothing
            Dim infoText As String = ""

            Await Task.Run(Sub()
                               Try
                                   Dim safePoly = imagePolygon.Select(Function(p) New Point(Math.Max(0, Math.Min(srcClone.Width - 1, p.X)), Math.Max(0, Math.Min(srcClone.Height - 1, p.Y)))).ToArray()
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
                                               Dim quality = If(density > 0.15, "✅ " & LanguageManager.T("Train_QualityRich"), If(density > 0.05, "🟡 " & LanguageManager.T("Train_QualityMedium"), "🔴 " & LanguageManager.T("Train_QualitySparse")))
                                               infoText = $"{LanguageManager.T("Train_EdgeDensity")}{vbCrLf}{density:F3}{vbCrLf}{quality}"
                                           End Using
                                       End Using
                                   End Using
                               Catch
                                   infoText = LanguageManager.T("Train_PreviewFailed")
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
            MeteorMessageBox.Show(LanguageManager.T("Train_ErrManageFailed") & ex.Message)
        End Try
    End Sub

    Private Sub BtnClose_Click(sender As Object, e As RoutedEventArgs)
        Me.Close()
    End Sub

    Protected Overrides Sub OnClosed(e As EventArgs)
        MyBase.OnClosed(e)
        _sourceMat?.Dispose()
        If Not String.IsNullOrWhiteSpace(_startedCameraId) Then
            CameraService.Instance.StopCamera(_startedCameraId)
            _startedCameraId = ""
        End If
    End Sub

    Public Sub RefreshLanguageUI()
        TrainWindow.Title = LanguageManager.T("Train_Title")
        TxtTitle.Text = LanguageManager.T("Train_Title")

        TxtPreviewPanelTitle.Text = LanguageManager.T("Train_PreviewPanelTitle")
        If Not _polygonClosed Then
            TxtPreviewInfo.Text = LanguageManager.T("Train_PreviewAutoPrompt")
        End If
        BtnRefreshPreview.Content = "🔄 " & LanguageManager.T("Train_BtnRefresh")

        TxtImageSourceSection.Text = LanguageManager.T("Train_ImageSourceSection")
        TxtSelectCamLabel.Text = LanguageManager.T("Train_SelectCamLabel")
        BtnCaptureFromCamera.Content = "📷 " & LanguageManager.T("Train_BtnCapture")
        BtnLoadImage.Content = "📁 " & LanguageManager.T("Train_BtnLoadFile")

        BtnAutoAnalyze.Content = "⚡ " & LanguageManager.T("Train_BtnAutoAnalyze")
        If Not TxtAutoInfo.Text.Contains("Canny") Then
            TxtAutoInfo.Text = LanguageManager.T("Train_AutoDesc")
        End If
        BtnClearPolygon.Content = LanguageManager.T("Train_BtnClearPolygon")

        If Not _polygonClosed AndAlso _polygonPoints.Count = 0 Then
            TxtRoiStatus.Text = LanguageManager.T("Train_RoiStatusDefault")
        End If
        RefreshSampleCount()
        ApplyMasterConfig()

        TxtPyramidLabel.Text = LanguageManager.T("Train_PyramidLabel")
        TxtMatchMethodLabel.Text = LanguageManager.T("Train_MatchMethodLabel")
        TxtMinAreaLabel.Text = LanguageManager.T("Train_MinAreaLabel")
        TxtCannyLowLabel.Text = LanguageManager.T("Train_CannyLowLabel")
        TxtCannyHighLabel.Text = LanguageManager.T("Train_CannyHighLabel")
        TxtAngleMinLabel.Text = LanguageManager.T("Train_AngleMinLabel")
        TxtAngleMaxLabel.Text = LanguageManager.T("Train_AngleMaxLabel")
        TxtAngleStepLabel.Text = LanguageManager.T("Train_AngleStepLabel")

        BtnAddSample.Content = LanguageManager.T("Train_BtnAddSample")
        TxtAddSampleDesc.Text = LanguageManager.T("Train_AddSampleDesc")
        BtnManageTemplates.Content = "📋 " & LanguageManager.T("Train_BtnManage")
        BtnClose.Content = LanguageManager.T("Train_BtnClose")

        LoadingText.Text = LanguageManager.T("Train_LoadingCamera")
    End Sub

End Class