namespace TradePlatform.Core.Constants
{
    public static class MessagingConstants
    {
        public const string OrdersQueue = "trade-orders";
        public const string OrdersDeadLetterExchange = "trade-orders.dlx";
        public const string OrdersDeadLetterQueue = "trade-orders.dead";
        public const string OrdersDeadLetterRoutingKey = "trade-orders.dead";

        public const string NotificationsQueue = "trade-notifications";
        public const string NotificationsExchange = "trade-notifications-x";

        public const string RetryHeader = "x-retry-count";
    }

    public static class TransactionStatus
    {
        public const string Pending = "Pending";
        public const string Processed = "Processed";
        public const string Failed = "Failed";
    }
}