import React, { useState, useEffect } from 'react';
import { Box, Typography, Alert, Snackbar, CircularProgress } from '@mui/material';
import { CheckCircle as SuccessIcon } from '@mui/icons-material';
import { useAuth } from '../../context/AuthContext';
import { useSignalR } from '../../context/SignalRContext';
import { 
    submitTransaction, 
    getMyAccount, 
    provisionAccount, 
    ApiValidationError,
    TransactionStatus
} from '../../services/api';
import { TransactionForm, TransactionFormData } from './TransactionForm';

export const Dashboard: React.FC = () => {
    const { token, userEmail, logout } = useAuth();
    const { connection } = useSignalR();
    
    const [lastId, setLastId] = useState<string | null>(null);
    const [notification, setNotification] = useState<string | null>(null);
    const [globalError, setGlobalError] = useState<string | null>(null);

    const [myAccountId, setMyAccountId] = useState<string>(''); 
    const [loadingAccount, setLoadingAccount] = useState(true);

    if (!token) return null;

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
                setNotification(`Transaction ${update.transactionId} is now ${statusLabel}!`);
            });
        }
        return () => { connection?.off("ReceiveStatusUpdate"); };
    }, [connection]);

    const handleSubmit = async (data: TransactionFormData, setErrors: (errors: any) => void) => {
        setGlobalError(null);
        setLastId(null);
        try {
            const result = await submitTransaction(data, token);
            setLastId(result.id);
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