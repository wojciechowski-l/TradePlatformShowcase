import { createContext, useContext, useState, ReactNode } from 'react';

interface AuthContextType {
    token: string | null;
    userEmail: string;
    isAuthenticated: boolean;
    login: (token: string, email: string) => void;
    logout: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const useAuth = () => {
    const context = useContext(AuthContext);
    if (!context) {
        throw new Error('useAuth must be used within an AuthProvider');
    }
    return context;
};

export const AuthProvider = ({ children }: { children: ReactNode }) => {
    const [token, setToken] = useState<string | null>(null);
    const [userEmail, setUserEmail] = useState<string>('');

    const login = (newToken: string, email: string) => {
        setToken(newToken);
        setUserEmail(email);
    };

    const logout = () => {
        setToken(null);
        setUserEmail('');
    };

    return (
        <AuthContext.Provider value={{
            token,
            userEmail,
            isAuthenticated: !!token,
            login,
            logout
        }}>
            {children}
        </AuthContext.Provider>
    );
};