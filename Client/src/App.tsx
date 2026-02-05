import { Container, Paper, CssBaseline } from '@mui/material';
import { SignalRProvider } from './context/SignalRContext';
import { AuthProvider, useAuth } from './context/AuthContext';
import { Header } from './components/Layout/Header';
import { AuthScreen } from './components/Auth/AuthScreen';
import { Dashboard } from './components/Dashboard/Dashboard';

const AppContent = () => {
  const { token, isAuthenticated } = useAuth();

  return (
    <SignalRProvider token={token}>
      <CssBaseline />
      <Header />

      <Container maxWidth="sm" sx={{ mt: 8 }}>
        <Paper elevation={3} sx={{ p: 4 }}>
          {isAuthenticated ? (
            <Dashboard />
          ) : (
            <AuthScreen />
          )}
        </Paper>
      </Container>
    </SignalRProvider>
  );
};

function App() {
  return (
    <AuthProvider>
      <AppContent />
    </AuthProvider>
  );
}

export default App;