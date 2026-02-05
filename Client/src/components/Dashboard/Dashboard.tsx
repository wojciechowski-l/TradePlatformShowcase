import React, { useState, useEffect } from 'react';
import { Box, Typography, Alert, Snackbar, CircularProgress } from '@mui/material';
import { CheckCircle as SuccessIcon } from '@mui/icons-material';
import { useSignalR } from '../../context/SignalRContext';
import { submitTransaction, getMyAccount, provisionAccount } from '../../services/api';
import { TransactionForm, TransactionFormData } from './TransactionForm';

interface DashboardProps {
    token: string;
    userEmail: string;
    onLogout: () => void;
}

export const Dashboard: React.FC<DashboardProps> = ({ token, userEmail, onLogout }) => {
    const { connection } = useSignalR();
    const [lastId, setLastId] = useState<string | null>(null);
    const [notification, setNotification] = useState<string | null>(null);
    const [globalError, setGlobalError] = useState<string | null>(null);

    const [myAccountId, setMyAccountId] = useState<string>(''); 
    const [loadingAccount, setLoadingAccount] = useState(true);
    
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
            } catch (err: any) {
                if (err.name === 'AbortError') return;

                console.warn("Fetch failed, attempting provision...", err);
                try {
                    const newAccount = await provisionAccount(token, controller.signal);
                    if (!controller.signal.aborted) {
                        setMyAccountId(newAccount.id);
                    }
                } catch (provErr: any) {
                    if (provErr.name !== 'AbortError') {
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
                    .catch(err => console.warn("SignalR Join Error:", err));
            };

            joinGroup();

            // Re-join on reconnection
            connection.onreconnected(joinGroup);
        }
    }, [connection, myAccountId]);

    useEffect(() => {
        if (connection) {
            connection.on("ReceiveStatusUpdate", (update: any) => {
                setNotification(`Transaction ${update.transactionId} is now ${update.status}!`);
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
        } catch (err: any) {
            if (err.message.includes("Unauthorized")) {
                onLogout();
            } else if (err.validationErrors) {
                setErrors(err.validationErrors);
            } else {
                setGlobalError(err.message);
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