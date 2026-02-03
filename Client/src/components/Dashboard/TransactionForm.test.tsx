import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TransactionForm } from './TransactionForm';
import { vi, describe, it, expect } from 'vitest';

describe('TransactionForm', () => {
    const mockSubmit = vi.fn();
    const initialSourceId = "SRC-123";

    it('renders with initial values', () => {
        render(<TransactionForm initialSourceId={initialSourceId} onSubmit={mockSubmit} />);
        
        const sourceInput = screen.getByLabelText(/source account/i);
        expect(sourceInput).toHaveValue(initialSourceId);

        expect(screen.getByLabelText(/amount/i)).toHaveValue(100);
    });

    it('calls onSubmit with form data when submitted', async () => {
        const user = userEvent.setup();
        render(<TransactionForm initialSourceId={initialSourceId} onSubmit={mockSubmit} />);

        const targetInput = screen.getByLabelText(/target account/i);
        await user.clear(targetInput);
        await user.type(targetInput, 'TARGET-ABC');

        const amountInput = screen.getByLabelText(/amount/i);
        await user.clear(amountInput);
        await user.type(amountInput, '500');

        await user.click(screen.getByRole('button', { name: /submit transaction/i }));

        expect(mockSubmit).toHaveBeenCalledTimes(1);
        expect(mockSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                sourceAccountId: initialSourceId,
                targetAccountId: 'TARGET-ABC',
                amount: 500
            }),
            expect.any(Function)
        );
    });

    it('displays validation errors passed from the parent', async () => {
        const user = userEvent.setup();

        const mockSubmitWithError = vi.fn(async (_, setErrors) => {
            setErrors({ amount: ['Insufficient funds'] });
        });

        render(<TransactionForm initialSourceId={initialSourceId} onSubmit={mockSubmitWithError} />);

        await user.click(screen.getByRole('button', { name: /submit transaction/i }));

        expect(await screen.findByText('Insufficient funds')).toBeInTheDocument();
    });
});