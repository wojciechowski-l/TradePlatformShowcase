import { useState } from 'react';
import { Container, Paper, CssBaseline } from '@mui/material';
import { SignalRProvider } from './context/SignalRContext';
import { Header } from './components/Layout/Header';
import { AuthScreen } from './components/Auth/AuthScreen';
import { Dashboard } from './components/Dashboard/Dashboard';

function App() {
  const [token, setToken] = useState<string | null>(null);
  const [userEmail, setUserEmail] = useState<string>('');

  const handleLoginSuccess = (accessToken: string, email: string) => {
    setToken(accessToken);
    setUserEmail(email);
  };

  const handleLogout = () => {
    setToken(null);
    setUserEmail('');
  };

  return (
    <SignalRProvider token={token}>
      <CssBaseline />
      <Header isAuthenticated={!!token} onLogout={handleLogout} />

      <Container maxWidth="sm" sx={{ mt: 8 }}>
        <Paper elevation={3} sx={{ p: 4 }}>
          {token ? (
            <Dashboard 
              token={token} 
              userEmail={userEmail} 
              onLogout={handleLogout} 
            />
          ) : (
            <AuthScreen onLoginSuccess={handleLoginSuccess} />
          )}
        </Paper>
      </Container>
    </SignalRProvider>
  );
}

export default App;