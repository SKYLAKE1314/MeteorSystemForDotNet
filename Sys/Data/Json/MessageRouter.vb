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
                OnEnd?.Invoke(t)   ' t.TaskStatus 仍為 0，ExecuteTask 會走 Case 0（啟動/預熱）分支

            Case 1
                OnPause?.Invoke(t) ' t.TaskStatus=1，ExecuteTask 會走 Case 1（暫停）分支

            Case 2
                OnResume?.Invoke(t) ' t.TaskStatus=2，ExecuteTask 會走 Case 2（恢復）分支

            Case 3
                OnEnd?.Invoke(t)

        End Select

    End Sub

    Private Function ParseTask(msg As VATJsonObject) As TaskData

        Dim taskStatusText = Safe(msg, "taskStatus")
        Dim taskStatus As Integer

        If Not Integer.TryParse(taskStatusText, taskStatus) Then
            Return Nothing
        End If

        Dim t As New TaskData

        t.RequestId = Safe(msg, "requestId")

        t.StationId = Safe(msg, "stationId")

        t.TaskStatus = taskStatus

        t.PartCode = Safe(msg, "partCode")

        t.SupplierCode = Safe(msg, "supplierCode")

        t.PartCount = ToInt(msg, "partCount")

        t.BatchNo = Safe(msg, "batchNo")

        Return t

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
