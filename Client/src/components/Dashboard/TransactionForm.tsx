import React, { useState } from 'react';
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

export const TransactionForm: React.FC<TransactionFormProps> = ({ initialSourceId, onSubmit }) => {
  const [formData, setFormData] = useState<TransactionFormData>({
    sourceAccountId: initialSourceId,
    targetAccountId: 'ACC-999',
    amount: 100,
    currency: 'USD'
  });

  const [loading, setLoading] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  React.useEffect(() => {
    setFormData(prev => ({ ...prev, sourceAccountId: initialSourceId }));
  }, [initialSourceId]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setFieldErrors({});

    try {
      await onSubmit(formData, setFieldErrors);
    } catch (error) {
      console.error("Form submission error", error);
    } finally {
      setLoading(false);
    }
  };

  const getFieldError = (field: string) => {
    const errorList = fieldErrors[field] || fieldErrors[field.charAt(0).toUpperCase() + field.slice(1)];
    return errorList ? errorList[0] : undefined;
  };

  return (
    <form onSubmit={handleSubmit}>
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, sm: 6 }}>
          <TextField
            fullWidth
            label="Source Account"
            value={formData.sourceAccountId}
            onChange={(e) => setFormData({ ...formData, sourceAccountId: e.target.value })}
            error={!!getFieldError('sourceAccountId')}
            helperText={getFieldError('sourceAccountId')}
            disabled={loading}
          />
        </Grid>
        <Grid size={{ xs: 12, sm: 6 }}>
          <TextField
            fullWidth
            label="Target Account"
            value={formData.targetAccountId}
            onChange={(e) => setFormData({ ...formData, targetAccountId: e.target.value })}
            error={!!getFieldError('targetAccountId')}
            helperText={getFieldError('targetAccountId')}
            disabled={loading}
          />
        </Grid>
        <Grid size={{ xs: 8 }}>
          <TextField
            fullWidth
            type="number"
            label="Amount"
            value={formData.amount}
            onChange={(e) => setFormData({ ...formData, amount: Number(e.target.value) })}
            error={!!getFieldError('amount')}
            helperText={getFieldError('amount')}
            disabled={loading}
          />
        </Grid>
        <Grid size={{ xs: 4 }}>
          <TextField
            fullWidth
            label="Currency"
            value={formData.currency}
            onChange={(e) => setFormData({ ...formData, currency: e.target.value })}
            error={!!getFieldError('currency')}
            helperText={getFieldError('currency')}
            disabled={loading}
          />
        </Grid>
      </Grid>

      <Button
        fullWidth
        variant="contained"
        size="large"
        type="submit"
        disabled={loading}
        startIcon={loading ? <CircularProgress size={20} color="inherit" /> : <SendIcon />}
        sx={{ mt: 4 }}
      >
        {loading ? 'Processing...' : 'Submit Transaction'}
      </Button>
    </form>
  );
};