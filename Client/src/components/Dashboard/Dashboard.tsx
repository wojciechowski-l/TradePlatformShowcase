import React, { useState, useEffect, useRef } from 'react';
import { Box, Typography, Alert, Snackbar, CircularProgress, Chip, Divider, List, ListItem, ListItemText, Stack } from '@mui/material';
import { CheckCircle as SuccessIcon } from '@mui/icons-material';
import { useAuth } from '../../context/AuthContext';
import { useSignalR } from '../../context/SignalRContext';
import { createRequestId } from '../../utils/ids';
import { 
    submitTransaction, 
    getMyAccountActivity,
    getMyAccount, 
    provisionAccount, 
    ApiValidationError,
    TransactionStatus,
    AccountActivityDto,
    AccountActivityDirection
} from '../../services/api';
import { TransactionForm, TransactionFormData } from './TransactionForm';

export const Dashboard: React.FC = () => {
    const { token, userEmail, logout } = useAuth();
    const { connection } = useSignalR();
    
    const [lastId, setLastId] = useState<string | null>(null);
    const [notification, setNotification] = useState<string | null>(null);
    const [globalError, setGlobalError] = useState<string | null>(null);
    const [activityFeed, setActivityFeed] = useState<AccountActivityDto[]>([]);
    const [feedError, setFeedError] = useState<string | null>(null);
    const [isRefreshingFeed, setIsRefreshingFeed] = useState(false);
    const [pendingProjectionId, setPendingProjectionId] = useState<string | null>(null);

    const [myAccountId, setMyAccountId] = useState<string>(''); 
    const [loadingAccount, setLoadingAccount] = useState(true);

    const idempotencyKeyRef = useRef(createRequestId());

    const isTerminalStatus = (status: TransactionStatus) =>
        status === TransactionStatus.Processed || status === TransactionStatus.Failed;

    const getStatusChipColor = (status: TransactionStatus): 'default' | 'success' | 'warning' | 'error' => {
        switch (status) {
            case TransactionStatus.Processed:
                return 'success';
            case TransactionStatus.Failed:
                return 'error';
            case TransactionStatus.Validated:
            case TransactionStatus.Processing:
                return 'warning';
            default:
                return 'default';
        }
    };

    if (!token) return null;

    const refreshActivityFeed = async (signal?: AbortSignal) => {
        setIsRefreshingFeed(true);
        setFeedError(null);

        try {
            const items = await getMyAccountActivity(token, signal);
            setActivityFeed(items);

            if (pendingProjectionId && items.some(item =>
                item.transactionId === pendingProjectionId && isTerminalStatus(item.status))) {
                setPendingProjectionId(null);
            }
        } catch (err: unknown) {
            const isAbort = err instanceof Error && err.name === 'AbortError';
            if (!isAbort) {
                setFeedError("Could not refresh the activity projection.");
            }
        } finally {
            if (!signal?.aborted) {
                setIsRefreshingFeed(false);
            }
        }
    };

    useEffect(() => {
        const controller = new AbortController();

        const initAccount = async () => {
            try {
                let account = await getMyAccount(token, controller.signal);

                if (!account) {
                    console.log("Account missing. Provisioning...");
                    account = await provisionAccount(token, controller.signal);
                }
                
                if (!controller.signal.aborted) {
                    setMyAccountId(account.id);
                }
            } catch (err: unknown) {
                if (err instanceof Error && err.name === 'AbortError') return;

                console.warn("Fetch failed, attempting provision...", err);
                try {
                    const newAccount = await provisionAccount(token, controller.signal);
                    if (!controller.signal.aborted) {
                        setMyAccountId(newAccount.id);
                    }
                } catch (provErr: unknown) {
                    const isAbort = provErr instanceof Error && provErr.name === 'AbortError';
                    if (!isAbort) {
                        setGlobalError("Could not load or create your account. Please refresh.");
                    }
                }
            } finally {
                if (!controller.signal.aborted) {
                    setLoadingAccount(false);
                }
            }
        };
        initAccount();

        return () => {
            controller.abort();
        };
    }, [token]);

    useEffect(() => {
        if (!myAccountId) return;

        const controller = new AbortController();
        void refreshActivityFeed(controller.signal);

        return () => {
            controller.abort();
        };
    }, [myAccountId]);

    useEffect(() => {
        if (connection && myAccountId) {
            const joinGroup = () => {
                connection.invoke("JoinAccountGroup", myAccountId)
                    .then(() => console.log(`Joined group: ${myAccountId}`))
                    .catch((err: unknown) => console.warn("SignalR Join Error:", err));
            };

            joinGroup();
            connection.onreconnected(joinGroup);
        }
    }, [connection, myAccountId]);

    useEffect(() => {
        if (connection) {
            connection.on("ReceiveStatusUpdate", (update: any) => {
                const statusLabel = TransactionStatus[update.status] || update.status;
                const failureSuffix = update.failureReason ? ` Reason: ${update.failureReason}` : '';
                setNotification(`Transaction ${update.transactionId} is now ${statusLabel}!${failureSuffix}`);
                void refreshActivityFeed();
            });
        }
        return () => { connection?.off("ReceiveStatusUpdate"); };
    }, [connection]);

    useEffect(() => {
        if (!pendingProjectionId) return;

        let attempts = 0;
        const interval = window.setInterval(() => {
            attempts += 1;
            void refreshActivityFeed();

            if (attempts >= 12) {
                window.clearInterval(interval);
            }
        }, 1000);

        return () => {
            window.clearInterval(interval);
        };
    }, [pendingProjectionId]);

    const handleSubmit = async (data: TransactionFormData, setErrors: (errors: any) => void) => {
        setGlobalError(null);
        setLastId(null);
        try {
            const result = await submitTransaction(data, token, idempotencyKeyRef.current);
            setLastId(result.id);
            setPendingProjectionId(result.id);
            idempotencyKeyRef.current = createRequestId();
        } catch (err: unknown) {
            if (err instanceof ApiValidationError) {
                setErrors(err.validationErrors);
            } else if (err instanceof Error) {
                if (err.message.includes("Unauthorized")) {
                    logout();
                } else {
                    setGlobalError(err.message);
                }
            } else {
                setGlobalError("An unexpected error occurred.");
            }
        }
    };

    if (loadingAccount) return <Box sx={{ p: 4, textAlign: 'center' }}><CircularProgress /></Box>;

    return (
        <Box>
            <Box sx={{ mb: 3, textAlign: 'center' }}>
                <Typography variant="h5" gutterBottom>New Transaction</Typography>
                <Typography variant="body2" color="text.secondary">Welcome, {userEmail}</Typography>
            </Box>

            {globalError && <Alert severity="error" sx={{ mb: 3 }}>{globalError}</Alert>}

            <TransactionForm initialSourceId={myAccountId} onSubmit={handleSubmit} />

            {lastId && (
                <Alert icon={<SuccessIcon fontSize="inherit" />} severity="success" sx={{ mt: 3 }}>
                    <strong>Success!</strong> Transaction ID: {lastId}
                </Alert>
            )}

            {pendingProjectionId && (
                <Alert severity="info" sx={{ mt: 3 }}>
                    Transaction accepted. The activity feed is waiting for the async projection to catch up.
                </Alert>
            )}

            <Box sx={{ mt: 4 }}>
                <Stack direction="row" spacing={1} sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
                    <Typography variant="h6">Activity Feed</Typography>
                    {isRefreshingFeed && <CircularProgress size={18} />}
                </Stack>

                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                    This list is served from the read model and updates asynchronously from bus events.
                </Typography>

                {feedError && <Alert severity="warning" sx={{ mb: 2 }}>{feedError}</Alert>}

                {activityFeed.length === 0 ? (
                    <Alert severity="info">No projected activity yet. Submit a transaction to populate the read side.</Alert>
                ) : (
                    <List disablePadding>
                        {activityFeed.map((item, index) => (
                            <React.Fragment key={`${item.transactionId}-${item.direction}`}>
                                <ListItem disableGutters sx={{ py: 1.5 }}>
                                    <ListItemText
                                        primary={
                                            <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                                                <Typography variant="subtitle2">
                                                    {item.direction === AccountActivityDirection.Outgoing ? 'Outgoing' : 'Incoming'} {item.amount} {item.currency}
                                                </Typography>
                                                <Chip
                                                    size="small"
                                                    label={TransactionStatus[item.status]}
                                                    color={getStatusChipColor(item.status)}
                                                    variant="outlined"
                                                />
                                            </Stack>
                                        }
                                        secondary={
                                            <>
                                                <Typography component="span" variant="body2" color="text.primary">
                                                    Counterparty: {item.counterpartyAccountId}
                                                </Typography>
                                                <br />
                                                <Typography component="span" variant="caption" color="text.secondary">
                                                    Tx {item.transactionId} · Created {new Date(item.createdAtUtc).toLocaleString()}
                                                </Typography>
                                                <br />
                                                <Typography component="span" variant="caption" color="text.secondary">
                                                    Last projection event {new Date(item.lastEventUtc).toLocaleString()}
                                                </Typography>
                                                {item.failureReason && (
                                                    <>
                                                        <br />
                                                        <Typography component="span" variant="caption" color="error.main">
                                                            Failure reason: {item.failureReason}
                                                        </Typography>
                                                    </>
                                                )}
                                            </>
                                        }
                                    />
                                </ListItem>
                                {index < activityFeed.length - 1 && <Divider component="li" />}
                            </React.Fragment>
                        ))}
                    </List>
                )}
            </Box>

            <Snackbar 
                open={!!notification} autoHideDuration={6000} 
                onClose={() => setNotification(null)}
                anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
            >
                <Alert onClose={() => setNotification(null)} severity="info" sx={{ width: '100%' }}>
                    {notification}
                </Alert>
            </Snackbar>
        </Box>
    );
};
