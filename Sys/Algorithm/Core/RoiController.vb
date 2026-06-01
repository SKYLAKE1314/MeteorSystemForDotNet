Imports OpenCvSharp
Imports System.Windows
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Shapes

Imports CvRect = OpenCvSharp.Rect
Imports CvPoint = OpenCvSharp.Point
Imports WpfPoint = System.Windows.Point

Public Class RoiController

    Private _canvas As InkCanvas
    Private _image As System.Windows.Controls.Image
    Private _srcMat As Mat

    Private _drawing As Boolean
    Private _startPoint As WpfPoint
    Private _rect As Rectangle

    Public Property Roi As CvRect

    Public Sub New(canvas As InkCanvas,
                   image As System.Windows.Controls.Image,
                   src As Mat)

        _canvas = canvas
        _image = image
        _srcMat = src

    End Sub

    ' =========================
    ' MouseDown
    ' =========================
    Public Sub MouseDown(e As MouseButtonEventArgs)

        If _srcMat Is Nothing Then Return

        _canvas.Children.Clear()

        _drawing = True

        _startPoint = e.GetPosition(_canvas)

        _rect = New Rectangle With {
        .Stroke = Brushes.Red,
        .StrokeThickness = 2
    }

        InkCanvas.SetLeft(_rect, _startPoint.X)
        InkCanvas.SetTop(_rect, _startPoint.Y)

        _canvas.Children.Add(_rect)

    End Sub

    ' =========================
    ' MouseMove
    ' =========================
    Public Sub MouseMove(e As MouseEventArgs)

        If Not _drawing Then Return
        If _rect Is Nothing Then Return

        Dim pos As WpfPoint = e.GetPosition(_canvas)

        Dim x = Math.Min(pos.X, _startPoint.X)
        Dim y = Math.Min(pos.Y, _startPoint.Y)
        Dim w = Math.Abs(pos.X - _startPoint.X)
        Dim h = Math.Abs(pos.Y - _startPoint.Y)

        InkCanvas.SetLeft(_rect, x)
        InkCanvas.SetTop(_rect, y)

        _rect.Width = w
        _rect.Height = h

    End Sub

    ' =========================
    ' MouseUp
    ' =========================
    Public Sub MouseUp()

        If _srcMat Is Nothing Then Return
        If _rect Is Nothing Then Return

        _drawing = False

        ' ROI 在 Canvas 座標
        Dim p As WpfPoint = _rect.TranslatePoint(
        New WpfPoint(0, 0),
        _image
    )

        Dim x = p.X
        Dim y = p.Y

        Dim w = _rect.Width
        Dim h = _rect.Height

        ' 直接映射比例（因為 Image = Fill）
        Dim scaleX As Double = _srcMat.Width / _image.ActualWidth
        Dim scaleY As Double = _srcMat.Height / _image.ActualHeight

        Roi = New CvRect(
        CInt(x * scaleX),
        CInt(y * scaleY),
        CInt(w * scaleX),
        CInt(h * scaleY)
    )

    End Sub

End Class
