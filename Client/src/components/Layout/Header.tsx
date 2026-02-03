import React from 'react';
import { AppBar, Toolbar, Typography, Button } from '@mui/material';

interface HeaderProps {
    isAuthenticated: boolean;
    onLogout: () => void;
}

export const Header: React.FC<HeaderProps> = ({ isAuthenticated, onLogout }) => (
    <AppBar position="static">
        <Toolbar>
            <Typography variant="h6" sx={{ flexGrow: 1 }}>
                TradePlatform Enterprise
            </Typography>
            {isAuthenticated && (
                <Button color="inherit" onClick={onLogout}>
                    Logout
                </Button>
            )}
        </Toolbar>
    </AppBar>
);