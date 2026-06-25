Imports VAT.Common

Public Class TaskRouter

    Public Property OnStart As Action(Of TaskData)
    Public Property OnPause As Action(Of TaskData)
    Public Property OnResume As Action(Of TaskData)
    Public Property OnEnd As Action(Of TaskData)

    Public Sub Route(msg As VATJsonObject)

        Dim t = ParseTask(msg)

        If t Is Nothing Then Exit Sub

        Select Case t.TaskStatus

            Case 0
                OnStart?.Invoke(t)

            Case 1
                OnPause?.Invoke(t)

            Case 2
                OnResume?.Invoke(t)

            Case 3
                OnEnd?.Invoke(t)

        End Select

    End Sub

    ' =========================
    ' SAFE PARSER（核心修復）
    ' =========================
    Private Function ParseTask(msg As VATJsonObject) As TaskData

        Try
            Dim t As New TaskData()

            t.RequestId = Safe(msg, "requestId")
            t.TaskStatus = ToInt(msg, "taskStatus")

            t.PartCode = Safe(msg, "partCode")
            t.SupplierCode = Safe(msg, "supplierCode")

            t.PartCount = ToInt(msg, "partCount")
            t.BatchNo = Safe(msg, "batchNo")

            ' 如果不是 task message，直接忽略
            If t.RequestId = "" Then Return Nothing

            Return t

        Catch ex As Exception
            ' 不要彈窗（避免 server 被 log 打爆）
            Return Nothing
        End Try

    End Function

    ' =========================
    ' SAFE GET STRING
    ' =========================
    Private Function Safe(msg As VATJsonObject, key As String) As String
        Try
            Dim v = msg(key)
            If v Is Nothing Then Return ""
            Return v.ToString()
        Catch
            Return ""
        End Try
    End Function

    ' =========================
    ' SAFE INT PARSE
    ' =========================
    Private Function ToInt(msg As VATJsonObject, key As String) As Integer
        Dim s = Safe(msg, key)
        Dim result As Integer

        If Integer.TryParse(s, result) Then
            Return result
        End If

        Return 0
    End Function

End Class