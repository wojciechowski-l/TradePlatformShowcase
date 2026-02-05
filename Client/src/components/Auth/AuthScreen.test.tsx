import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AuthScreen } from './AuthScreen';
import * as api from '../../services/api';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { useAuth } from '../../context/AuthContext';

vi.mock('../../services/api');
vi.mock('../../context/AuthContext');

describe('AuthScreen', () => {
    const mockLogin = vi.fn();

    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(useAuth).mockReturnValue({
            login: mockLogin,
            logout: vi.fn(),
            token: null,
            userEmail: '',
            isAuthenticated: false
        });
    });

    it('defaults to Sign In mode', () => {
        render(<AuthScreen />);
        expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
    });

    it('toggles to Register mode when link is clicked', async () => {
        const user = userEvent.setup();
        render(<AuthScreen />);

        const toggleBtn = screen.getByText(/need an account\? register/i);
        await user.click(toggleBtn);

        expect(screen.getByRole('heading', { name: /create account/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /register/i })).toBeInTheDocument();
    });

    it('calls login API and notifies context on success', async () => {
        const user = userEvent.setup();
        
        const mockResponse = { accessToken: 'fake-jwt', tokenType: 'Bearer', expiresIn: 3600, refreshToken: 'ref' };
        vi.mocked(api.loginUser).mockResolvedValue(mockResponse);

        render(<AuthScreen />);

        await user.type(screen.getByLabelText(/email/i), 'test@test.com');
        await user.type(screen.getByLabelText(/password/i), 'Password123!');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        expect(api.loginUser).toHaveBeenCalledWith({
            email: 'test@test.com',
            password: 'Password123!'
        });

        await waitFor(() => {
            expect(mockLogin).toHaveBeenCalledWith('fake-jwt', 'test@test.com');
        });
    });

    it('displays success message after registration', async () => {
        const user = userEvent.setup();

        vi.mocked(api.registerUser).mockResolvedValue(undefined);

        render(<AuthScreen />);

        await user.click(screen.getByText(/need an account\? register/i));

        await user.type(screen.getByLabelText(/email/i), 'new@test.com');
        await user.type(screen.getByLabelText(/password/i), 'Password123!');
        await user.click(screen.getByRole('button', { name: /register/i }));

        expect(api.registerUser).toHaveBeenCalledWith({
            email: 'new@test.com',
            password: 'Password123!'
        });

        expect(await screen.findByRole('alert')).toHaveTextContent(/registration successful/i);

        expect(await screen.findByRole('heading', { name: /sign in/i })).toBeInTheDocument();
    });

    it('displays error alert when login fails', async () => {
        const user = userEvent.setup();
        
        vi.mocked(api.loginUser).mockRejectedValue(new Error('Invalid Credentials'));

        render(<AuthScreen />);

        await user.type(screen.getByLabelText(/email/i), 'wrong@test.com');
        await user.type(screen.getByLabelText(/password/i), 'wrong');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        expect(await screen.findByRole('alert')).toHaveTextContent('Invalid Credentials');
        expect(mockLogin).not.toHaveBeenCalled();
    });
});