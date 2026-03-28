import React, { useEffect, useState } from 'react';
import { 
  Grid,
  TextField, 
  Button, 
  CircularProgress 
} from '@mui/material';
import { Send as SendIcon } from '@mui/icons-material';

export interface TransactionFormData {
  sourceAccountId: string;
  targetAccountId: string;
  amount: number;
  currency: string;
}

interface TransactionFormProps {
  initialSourceId: string;
  onSubmit: (data: TransactionFormData, setErrors: (errors: Record<string, string[]>) => void) => Promise<void>;
}

interface FormState {
  errors: Record<string, string[]>;
}

export const TransactionForm: React.FC<TransactionFormProps> = ({ initialSourceId, onSubmit }) => {
  const [state, setState] = useState<FormState>({ errors: {} });
  const [isPending, setIsPending] = useState(false);
  const [formData, setFormData] = useState<TransactionFormData>({
    sourceAccountId: initialSourceId,
    targetAccountId: 'ACC-999',
    amount: 100,
    currency: 'USD',
  });

  useEffect(() => {
    setFormData(current => ({ ...current, sourceAccountId: initialSourceId }));
  }, [initialSourceId]);

  const handleChange = (field: keyof TransactionFormData) =>
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const value = field === 'amount'
        ? Number(event.target.value)
        : event.target.value;

      setFormData(current => ({
        ...current,
        [field]: value,
      }));
    };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIsPending(true);
    setState({ errors: {} });

    let validationErrors: Record<string, string[]> = {};

    const setErrorsAdapter = (errors: Record<string, string[]>) => {
      validationErrors = errors;
    };

    try {
      await onSubmit(formData, setErrorsAdapter);
    } catch (error: unknown) {
      console.error("Form submission error", error);
    } finally {
      setState({ errors: validationErrors });
      setIsPending(false);
    }
  };

  const getFieldError = (field: string) => {
    const errorList = state.errors[field] || state.errors[field.charAt(0).toUpperCase() + field.slice(1)];
    return errorList ? errorList[0] : undefined;
  };

  return (
    <form onSubmit={handleSubmit}>
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, sm: 6 }}>
          <TextField
            fullWidth
            name="sourceAccountId"
            label="Source Account"
            value={formData.sourceAccountId}
            onChange={handleChange('sourceAccountId')}
            error={!!getFieldError('sourceAccountId')}
            helperText={getFieldError('sourceAccountId')}
            disabled={isPending}
          />
        </Grid>
        <Grid size={{ xs: 12, sm: 6 }}>
          <TextField
            fullWidth
            name="targetAccountId"
            label="Target Account"
            value={formData.targetAccountId}
            onChange={handleChange('targetAccountId')}
            error={!!getFieldError('targetAccountId')}
            helperText={getFieldError('targetAccountId')}
            disabled={isPending}
          />
        </Grid>
        <Grid size={{ xs: 8 }}>
          <TextField
            fullWidth
            name="amount"
            type="number"
            label="Amount"
            value={formData.amount}
            onChange={handleChange('amount')}
            error={!!getFieldError('amount')}
            helperText={getFieldError('amount')}
            disabled={isPending}
          />
        </Grid>
        <Grid size={{ xs: 4 }}>
          <TextField
            fullWidth
            name="currency"
            label="Currency"
            value={formData.currency}
            onChange={handleChange('currency')}
            error={!!getFieldError('currency')}
            helperText={getFieldError('currency')}
            disabled={isPending}
          />
        </Grid>
      </Grid>

      <Button
        fullWidth
        variant="contained"
        size="large"
        type="submit"
        disabled={isPending}
        startIcon={isPending ? <CircularProgress size={20} color="inherit" /> : <SendIcon />}
        sx={{ mt: 4 }}
      >
        {isPending ? 'Processing...' : 'Submit Transaction'}
      </Button>
    </form>
  );
};
