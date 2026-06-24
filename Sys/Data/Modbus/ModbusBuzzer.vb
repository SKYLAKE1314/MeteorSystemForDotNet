Imports Modbus.Device
Imports System.Net.Sockets
Imports System.Threading

Namespace IoBoard

    Public Class ModbusBuzzer
        Implements IDisposable

        Private ReadOnly _ip As String
        Private ReadOnly _port As Integer
        Private ReadOnly _unitId As Byte
        Private ReadOnly _coilBaseAddress As UShort

        Private _client As TcpClient
        Private _master As ModbusIpMaster

        Private ReadOnly _sync As New Object()
        Private _disposed As Boolean = False

        Public Property Logger As Action(Of String)

        ''' <summary>
        ''' 建構
        ''' </summary>
        Public Sub New(ip As String,
                       Optional port As Integer = 502,
                       Optional unitId As Byte = 1,
                       Optional coilBaseAddress As UShort = 0)

            If ip Is Nothing Then
                Throw New ArgumentNullException(NameOf(ip))
            End If

            _ip = ip
            _port = port
            _unitId = unitId
            _coilBaseAddress = coilBaseAddress
        End Sub

        ''' <summary>
        ''' 建立連線
        ''' </summary>
        Public Sub Connect(Optional connectTimeoutMs As Integer = 13000)

            SyncLock _sync

                EnsureNotDisposed()

                If _client IsNot Nothing AndAlso _client.Connected Then
                    Logger?.Invoke("已存在連線，跳過 Connect()")
                    Return
                End If

                If _client IsNot Nothing Then
                    _client.Close()
                End If

                _client = New TcpClient()

                Dim ar = _client.BeginConnect(_ip, _port, Nothing, Nothing)

                Dim ok As Boolean = ar.AsyncWaitHandle.WaitOne(connectTimeoutMs)

                If Not ok OrElse Not _client.Connected Then

                    _client.Close()
                    _client = Nothing

                    ErrorDialogHelper.ShowError(
                        $"無法連線到 {_ip}:{_port}（timeout {connectTimeoutMs} ms）")
                End If


                _client.EndConnect(ar)

                _master = ModbusIpMaster.CreateIp(_client)

                _master.Transport.ReadTimeout = 2000
                _master.Transport.WriteTimeout = 2000

                Logger?.Invoke(
                    $"已連線 {_ip}:{_port}，UnitId={_unitId}")

            End SyncLock

        End Sub

        ''' <summary>
        ''' 中斷連線
        ''' </summary>
        Public Sub Disconnect()

            SyncLock _sync

                If _master IsNot Nothing Then
                    Try
                        _master.Dispose()
                    Catch
                    End Try

                    _master = Nothing
                End If

                If _client IsNot Nothing Then
                    Try
                        _client.Close()
                    Catch
                    End Try

                    _client = Nothing
                End If

            End SyncLock

        End Sub

        ''' <summary>
        ''' 設定 DO
        ''' </summary>
        Public Sub SetDO(channelOneBased As Integer, onState As Boolean)

            If channelOneBased <= 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(channelOneBased))
            End If

            Dim coilAddr As UShort =
                CUShort(_coilBaseAddress + (channelOneBased - 1))

            SetCoil(coilAddr, onState)

        End Sub

        ''' <summary>
        ''' 直接寫 Coil
        ''' </summary>
        Public Sub SetCoil(coilAddress As UShort, onState As Boolean)

            SyncLock _sync

                EnsureNotDisposed()

                If _master Is Nothing Then
                    Connect()
                End If

                Try

                    _master.WriteSingleCoil(
                        _unitId,
                        coilAddress,
                        onState)

                Catch ex As Exception

                    Try
                        Disconnect()
                    Catch
                    End Try

                    Throw

                End Try

            End SyncLock

        End Sub

        ''' <summary>
        ''' 蜂鳴一次
        ''' </summary>
        Public Sub BeepOnce(Optional channelOneBased As Integer = 1,
                            Optional durationMs As Integer = 2000,
                            Optional retryTimes As Integer = 1)

            If durationMs <= 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(durationMs))
            End If

            Dim tries As Integer = 0
            Dim lastEx As Exception = Nothing

            While tries <= retryTimes

                Try

                    SetDO(channelOneBased, True)

                    Thread.Sleep(durationMs)

                    SetDO(channelOneBased, False)

                    Return

                Catch ex As Exception

                    lastEx = ex
                    tries += 1

                    Logger?.Invoke(
                        $"觸發嘗試失敗，重試 {tries}/{retryTimes}，錯誤：{ex.Message}")

                    Thread.Sleep(100)

                End Try

            End While

            Throw New InvalidOperationException(
                "觸發最終失敗",
                lastEx)

        End Sub

        ''' <summary>
        ''' 讀取 DI
        ''' </summary>
        Public Function ReadDI(diIndex As Byte) As Boolean

            SyncLock _sync

                EnsureNotDisposed()

                If _master Is Nothing Then
                    Connect()
                End If

                Try

                    Dim values() As Boolean =
                        _master.ReadInputs(
                            _unitId,
                            diIndex,
                            1)

                    Return values.Length > 0 AndAlso values(0)

                Catch ex As Exception

                    Logger?.Invoke(
                        $"讀取 DI[{diIndex}] 失敗: {ex.Message}")

                    Try
                        Disconnect()
                    Catch
                    End Try

                    Throw

                End Try

            End SyncLock

        End Function

        Private Sub EnsureNotDisposed()

            If _disposed Then
                Throw New ObjectDisposedException(NameOf(ModbusBuzzer))
            End If

        End Sub

#Region "IDisposable"

        Public Sub Dispose() Implements IDisposable.Dispose

            SyncLock _sync

                If _disposed Then Return

                Disconnect()

                _disposed = True

            End SyncLock

        End Sub

#End Region

    End Class

End Namespace