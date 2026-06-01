Imports System.Windows
Imports System.Windows.Controls
Public Class ImageRenderHelper
    Public Shared Function GetImageRenderRect(img As Image) As Rect

        Dim controlW = img.ActualWidth
        Dim controlH = img.ActualHeight

        Dim source = TryCast(img.Source, System.Windows.Media.Imaging.BitmapSource)
        If source Is Nothing Then Return New Rect()

        Dim imgW = source.PixelWidth
        Dim imgH = source.PixelHeight

        Dim imageRatio = imgW / imgH
        Dim controlRatio = controlW / controlH

        Dim renderW As Double
        Dim renderH As Double
        Dim offsetX As Double
        Dim offsetY As Double

        If imageRatio > controlRatio Then

            renderW = controlW
            renderH = controlW / imageRatio

            offsetX = 0
            offsetY = (controlH - renderH) / 2

        Else

            renderH = controlH
            renderW = controlH * imageRatio

            offsetY = 0
            offsetX = (controlW - renderW) / 2

        End If

        Return New Rect(offsetX, offsetY, renderW, renderH)

    End Function
End Class
