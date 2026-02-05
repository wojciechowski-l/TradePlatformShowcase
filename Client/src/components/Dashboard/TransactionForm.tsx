import React, { useActionState } from 'react';
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
  
  const submitAction = async (_prevState: FormState, formData: FormData): Promise<FormState> => {
    const data: TransactionFormData = {
      sourceAccountId: formData.get('sourceAccountId') as string,
      targetAccountId: formData.get('targetAccountId') as string,
      amount: Number(formData.get('amount')),
      currency: formData.get('currency') as string,
    };

    let validationErrors: Record<string, string[]> = {};

    const setErrorsAdapter = (errors: Record<string, string[]>) => {
      validationErrors = errors;
    };

    try {
      await onSubmit(data, setErrorsAdapter);
    } catch (error: unknown) {
      console.error("Form submission error", error);
    }

    return { errors: validationErrors };
  };

  const [state, formAction, isPending] = useActionState(submitAction, { errors: {} });

  const getFieldError = (field: string) => {
    const errorList = state.errors[field] || state.errors[field.charAt(0).toUpperCase() + field.slice(1)];
    return errorList ? errorList[0] : undefined;
  };

  return (
    <form action={formAction}>
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, sm: 6 }}>
          <TextField
            fullWidth
            name="sourceAccountId"
            label="Source Account"
            defaultValue={initialSourceId}
            key={initialSourceId} 
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
            defaultValue="ACC-999"
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
            defaultValue={100}
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
            defaultValue="USD"
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