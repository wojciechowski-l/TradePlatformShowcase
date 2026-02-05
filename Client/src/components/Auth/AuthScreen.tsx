import React, { useState } from 'react';
import { 
    Box, TextField, Button, Typography, CircularProgress, 
    InputAdornment, IconButton, Alert, Snackbar
} from '@mui/material';
import { 
    Visibility, VisibilityOff, LockOutlined as LockIcon 
} from '@mui/icons-material';
import { useAuth } from '../../context/AuthContext';
import { loginUser, registerUser } from '../../services/api';

export const AuthScreen: React.FC = () => {
    const { login } = useAuth();
    const [isLoginMode, setIsLoginMode] = useState(true);
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [showPassword, setShowPassword] = useState(false);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);

    const handleSubmit = async (e: React.SubmitEvent<HTMLFormElement>) => {
        e.preventDefault();
        setLoading(true);
        setError(null);

        try {
            if (isLoginMode) {
                const result = await loginUser({ email, password });
                login(result.accessToken, email);
            } else {
                await registerUser({ email, password });
                setSuccessMessage("Registration Successful! Please log in.");
                setIsLoginMode(true);
            }
        } catch (err: unknown) {
            if (err instanceof Error) {
                setError(err.message);
            } else {
                setError("An unexpected error occurred");
            }
        } finally {
            setLoading(false);
        }
    };

    return (
        <Box component="form" onSubmit={handleSubmit} sx={{ textAlign: 'center' }}>
            <Box sx={{ mb: 2 }}>
                <LockIcon color="primary" sx={{ fontSize: 40 }} />
                <Typography variant="h5">{isLoginMode ? 'Sign In' : 'Create Account'}</Typography>
            </Box>

            {error && <Alert severity="error" sx={{ mb: 3 }}>{error}</Alert>}

            <TextField
                fullWidth margin="normal" label="Email" type="email" required
                value={email} onChange={(e) => setEmail(e.target.value)}
            />
            <TextField
                fullWidth margin="normal" label="Password"
                type={showPassword ? 'text' : 'password'}
                required
                value={password} onChange={(e) => setPassword(e.target.value)}
                slotProps={{
                    input: {
                        endAdornment: (
                            <InputAdornment position="end">
                                <IconButton onClick={() => setShowPassword(!showPassword)} edge="end">
                                    {showPassword ? <VisibilityOff /> : <Visibility />}
                                </IconButton>
                            </InputAdornment>
                        ),
                    }
                }}
            />

            <Button
                fullWidth variant="contained" size="large" type="submit"
                disabled={loading} sx={{ mt: 3, mb: 2 }}
            >
                {loading ? <CircularProgress size={24} /> : (isLoginMode ? 'Sign In' : 'Register')}
            </Button>

            <Button onClick={() => { setIsLoginMode(!isLoginMode); setError(null); }}>
                {isLoginMode ? "Need an account? Register" : "Have an account? Sign In"}
            </Button>

            <Snackbar
                open={!!successMessage}
                autoHideDuration={6000}
                onClose={() => setSuccessMessage(null)}
                anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
            >
                <Alert onClose={() => setSuccessMessage(null)} severity="success" sx={{ width: '100%' }}>
                    {successMessage}
                </Alert>
            </Snackbar>
        </Box>
    );
};