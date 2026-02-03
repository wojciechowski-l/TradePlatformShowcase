import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AuthScreen } from './AuthScreen';
import * as api from '../../services/api';
import { vi, describe, it, expect, beforeEach } from 'vitest';

vi.mock('../../services/api');

describe('AuthScreen', () => {
    const mockLoginSuccess = vi.fn();

    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('defaults to Sign In mode', () => {
        render(<AuthScreen onLoginSuccess={mockLoginSuccess} />);
        expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
    });

    it('toggles to Register mode when link is clicked', async () => {
        const user = userEvent.setup();
        render(<AuthScreen onLoginSuccess={mockLoginSuccess} />);

        const toggleBtn = screen.getByText(/need an account\? register/i);
        await user.click(toggleBtn);

        expect(screen.getByRole('heading', { name: /create account/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /register/i })).toBeInTheDocument();
    });

    it('calls login API and notifies parent on success', async () => {
        const user = userEvent.setup();
        
        const mockResponse = { accessToken: 'fake-jwt', tokenType: 'Bearer', expiresIn: 3600, refreshToken: 'ref' };
        vi.mocked(api.loginUser).mockResolvedValue(mockResponse);

        render(<AuthScreen onLoginSuccess={mockLoginSuccess} />);

        await user.type(screen.getByLabelText(/email/i), 'test@test.com');
        await user.type(screen.getByLabelText(/password/i), 'Password123!');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        expect(api.loginUser).toHaveBeenCalledWith({
            email: 'test@test.com',
            password: 'Password123!'
        });

        await waitFor(() => {
            expect(mockLoginSuccess).toHaveBeenCalledWith('fake-jwt', 'test@test.com');
        });
    });

    it('displays error alert when login fails', async () => {
        const user = userEvent.setup();
        
        vi.mocked(api.loginUser).mockRejectedValue(new Error('Invalid Credentials'));

        render(<AuthScreen onLoginSuccess={mockLoginSuccess} />);

        await user.type(screen.getByLabelText(/email/i), 'wrong@test.com');
        await user.type(screen.getByLabelText(/password/i), 'wrong');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        expect(await screen.findByRole('alert')).toHaveTextContent('Invalid Credentials');
        expect(mockLoginSuccess).not.toHaveBeenCalled();
    });
});