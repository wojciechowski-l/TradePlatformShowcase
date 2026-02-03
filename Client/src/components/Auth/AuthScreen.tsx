import React, { useState } from 'react';
import { 
    Box, TextField, Button, Typography, CircularProgress, 
    InputAdornment, IconButton, Alert 
} from '@mui/material';
import { 
    Visibility, VisibilityOff, LockOutlined as LockIcon 
} from '@mui/icons-material';
import { loginUser, registerUser } from '../../services/api';

interface AuthScreenProps {
    onLoginSuccess: (token: string, email: string) => void;
}

export const AuthScreen: React.FC<AuthScreenProps> = ({ onLoginSuccess }) => {
    const [isLoginMode, setIsLoginMode] = useState(true);
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [showPassword, setShowPassword] = useState(false);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const handleSubmit = async (e: React.SubmitEvent<HTMLFormElement>) => {
        e.preventDefault();
        setLoading(true);
        setError(null);

        try {
            if (isLoginMode) {
                const result = await loginUser({ email, password });
                onLoginSuccess(result.accessToken, email);
            } else {
                await registerUser({ email, password });
                alert("Registration Successful! Please log in.");
                setIsLoginMode(true);
            }
        } catch (err: any) {
            setError(err.message);
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
        </Box>
    );
};