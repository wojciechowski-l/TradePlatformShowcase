using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace TradePlatform.Infrastructure.Messaging
{
    public interface IRabbitMQConnection
    {
        Task<IChannel> CreateChannelAsync();
    }

    public class RabbitMQConnection : IRabbitMQConnection, IDisposable
    {
        private readonly ConnectionFactory _factory;
        private IConnection? _connection;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _disposed;

        public RabbitMQConnection(string connectionStringOrHost)
        {
            _factory = new ConnectionFactory();

            if (Uri.TryCreate(connectionStringOrHost, UriKind.Absolute, out var uri)
                && (uri.Scheme == "amqp" || uri.Scheme == "amqps"))
            {
                _factory.Uri = uri;
            }
            else
            {
                _factory.HostName = connectionStringOrHost;
            }
        }

        public async Task<IChannel> CreateChannelAsync()
        {
            var conn = Volatile.Read(ref _connection);

            if (conn == null || !conn.IsOpen)
            {
                await _lock.WaitAsync();
                try
                {
                    conn = _connection;
                    if (conn == null || !conn.IsOpen)
                    {
                        conn?.Dispose();
                        _connection = await _factory.CreateConnectionAsync();

                        conn = _connection;
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }

            try
            {
                return await (conn ?? throw new InvalidOperationException("Failed to establish RabbitMQ connection")).CreateChannelAsync();
            }
            catch (AlreadyClosedException)
            {
                await _lock.WaitAsync();
                try
                {
                    if (_connection == conn)
                    {
                        _connection?.Dispose();
                        _connection = null;
                    }
                }
                finally
                {
                    _lock.Release();
                }
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _connection?.Dispose();
                _disposed = true;
                _lock.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}