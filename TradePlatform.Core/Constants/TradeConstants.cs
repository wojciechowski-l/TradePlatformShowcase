namespace TradePlatform.Core.Constants
{
    public static class TradeDefaults
    {
        public const decimal InitialAccountBalance = 1000m;
    }

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

    public enum TransactionStatus
    {
        Pending,
        Validated,
        Processing,
        Processed,
        Failed
    }

    public enum AccountActivityDirection
    {
        Outgoing,
        Incoming
    }
}
