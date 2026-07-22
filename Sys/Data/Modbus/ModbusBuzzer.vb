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
        Private ReadOnly _mode As IoBoardMode
        Private ReadOnly Property HasHardware As Boolean
            Get
                Return _mode <> IoBoardMode.NONE
            End Get
        End Property
        Public Property Logger As Action(Of String)

        ''' <summary>
        ''' 建構
        ''' </summary>
        Public Sub New(ip As String,
                       Optional port As Integer = 502,
                       Optional unitId As Byte = 1,
                       Optional coilBaseAddress As UShort = 0,
                         Optional mode As IoBoardMode = IoBoardMode.IO)

            If ip Is Nothing Then
                Throw New ArgumentNullException(NameOf(ip))
            End If

            _ip = ip
            _port = port
            _unitId = unitId
            _coilBaseAddress = coilBaseAddress
            _mode = mode
        End Sub

        ''' <summary>
        ''' 建立連線
        ''' </summary>
        Public Sub Connect(Optional connectTimeoutMs As Integer = 3000)

            If Not HasHardware Then
                Logger?.Invoke("MODE=NONE，跳過 Connect")
                Return
            End If

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

                    Try
                        _client.Close()
                    Catch
                    End Try

                    _client = Nothing

                    MeteorMessageBox.ShowError(
                $"無法連線到 {_ip}:{_port}（timeout {connectTimeoutMs} ms）")

                    Return
                End If

                _client.EndConnect(ar)

                _master = ModbusIpMaster.CreateIp(_client)

                _master.Transport.ReadTimeout = 2000
                _master.Transport.WriteTimeout = 2000

                Logger?.Invoke($"已連線 {_ip}:{_port}，UnitId={_unitId}")

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

            If Not HasHardware Then Return

            If channelOneBased <= 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(channelOneBased))
            End If

            Dim coilAddr As UShort = CUShort(_coilBaseAddress + (channelOneBased - 1))

            SetCoil(coilAddr, onState)

        End Sub

        ''' <summary>
        ''' 直接寫 Coil
        ''' </summary>
        Public Sub SetCoil(coilAddress As UShort, onState As Boolean)

            If Not HasHardware Then Return

            SyncLock _sync

                EnsureNotDisposed()

                If Not HasHardware Then Return

                If _master Is Nothing Then
                    Connect()
                End If

                If _master Is Nothing Then
                    Logger?.Invoke("[Buzzer] 連線失敗，無法設定 Coil。")
                    Return
                End If

                Try
                    _master.WriteSingleCoil(_unitId, coilAddress, onState)

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
        Public Async Function BeepOnceAsync(Optional channelOneBased As Integer = 1,
                                            Optional durationMs As Integer = 2000,
                                            Optional retryTimes As Integer = 1) As Task

            If Not HasHardware Then Return

            If durationMs <= 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(durationMs))
            End If

            If retryTimes < 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(retryTimes))
            End If

            Dim tries As Integer = 0
            Dim lastEx As Exception = Nothing

            While tries <= retryTimes

                Try

                    SetDO(channelOneBased, True)

                    Await Task.Delay(durationMs).ConfigureAwait(False)

                    SetDO(channelOneBased, False)

                    Return

                Catch ex As Exception

                    lastEx = ex
                    tries += 1

                    Logger?.Invoke($"蜂鳴失敗 {tries}/{retryTimes}: {ex.Message}")


                End Try
                Await Task.Delay(100).ConfigureAwait(False)

            End While

            Throw New InvalidOperationException("蜂鳴失敗", lastEx)

        End Function

        ''' <summary>
        ''' 讀取 DI
        ''' </summary>
        Public Function ReadDI(diIndex As Byte) As Boolean

            SyncLock _sync

                EnsureNotDisposed()

                If _master Is Nothing Then
                    Connect()
                End If

                If _master Is Nothing Then
                    Logger?.Invoke($"[Buzzer] 連線失敗，無法讀取 DI[{diIndex}]。")
                    Return False
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
