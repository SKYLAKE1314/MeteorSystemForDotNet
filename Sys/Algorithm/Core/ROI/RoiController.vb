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

        Dim w = If(Double.IsNaN(_rect.Width), 0.0, _rect.Width)
        Dim h = If(Double.IsNaN(_rect.Height), 0.0, _rect.Height)

        ' ── 【核心修正】完美支持 Stretch="Uniform" 坐標映射 ──
        Dim matW As Double = _srcMat.Width
        Dim matH As Double = _srcMat.Height
        Dim imgW As Double = _image.ActualWidth
        Dim imgH As Double = _image.ActualHeight

        If imgW > 0 AndAlso imgH > 0 AndAlso matW > 0 AndAlso matH > 0 Then
            Dim matAspect As Double = matW / matH
            Dim imgAspect As Double = imgW / imgH

            Dim displayedW As Double
            Dim displayedH As Double
            Dim offsetX As Double = 0
            Dim offsetY As Double = 0

            If matAspect > imgAspect Then
                ' 寬度撐滿，上下有黑邊 (Paddings)
                displayedW = imgW
                displayedH = imgW / matAspect
                offsetY = (imgH - displayedH) / 2.0
            Else
                ' 高度撐滿，左右有黑邊 (Paddings)
                displayedH = imgH
                displayedW = imgH * matAspect
                offsetX = (imgW - displayedW) / 2.0
            End If

            ' 減去黑邊偏移，還原到實際顯示圖像區域的相對坐標
            Dim relX As Double = x - offsetX
            Dim relY As Double = y - offsetY

            ' 等比映射到 Mat 的真實像素坐標
            Dim finalX As Double = relX * (matW / displayedW)
            Dim finalY As Double = relY * (matH / displayedH)
            Dim finalW As Double = w * (matW / displayedW)
            Dim finalH As Double = h * (matH / displayedH)

            ' 防禦邊界溢出
            Roi = New CvRect(
                Math.Max(0, Math.Min(CInt(finalX), _srcMat.Width - 1)),
                Math.Max(0, Math.Min(CInt(finalY), _srcMat.Height - 1)),
                Math.Max(1, Math.Min(CInt(finalW), _srcMat.Width - CInt(finalX))),
                Math.Max(1, Math.Min(CInt(finalH), _srcMat.Height - CInt(finalY)))
            )
        Else
            Roi = New CvRect(0, 0, _srcMat.Width, _srcMat.Height)
        End If

    End Sub

End Class
