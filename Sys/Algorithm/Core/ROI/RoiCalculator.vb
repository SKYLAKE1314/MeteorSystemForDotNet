Imports OpenCvSharp

Public Class RoiCalculator

    Public Shared Function ConvertToImageRect(
        imageWidth As Integer,
        imageHeight As Integer,
        displayWidth As Double,
        displayHeight As Double,
        roiX As Double,
        roiY As Double,
        roiW As Double,
        roiH As Double) As Rect

        ' =========================
        ' 防呆
        ' =========================
        If displayWidth <= 0 Or displayHeight <= 0 Then
            Return New Rect(0, 0, 0, 0)
        End If

        ' =========================
        ' scale（基礎比例）
        ' =========================
        Dim scaleX As Double = imageWidth / displayWidth
        Dim scaleY As Double = imageHeight / displayHeight

        Dim realX As Double = roiX * scaleX
        Dim realY As Double = roiY * scaleY
        Dim realW As Double = roiW * scaleX
        Dim realH As Double = roiH * scaleY

        ' =========================
        ' clamp（邊界保護）
        ' =========================
        realX = Math.Max(0, realX)
        realY = Math.Max(0, realY)

        If realX + realW > imageWidth Then
            realW = imageWidth - realX
        End If

        If realY + realH > imageHeight Then
            realH = imageHeight - realY
        End If

        ' =========================
        ' 防止非法 rect
        ' =========================
        If realW <= 0 Or realH <= 0 Then
            Return New Rect(0, 0, 0, 0)
        End If

        Try
            Return New Rect(
    CInt(realX),
    CInt(realY),
    CInt(realW),
    CInt(realH)
)
        Catch ex As Exception
            Logger.Error("RoiCalculator 轉換失敗: " & ex.Message)
        End Try


    End Function

End Class