import { API_BASE_URL } from '../config';

export interface LoginRequest {
    email: string;
    password: string;
}

export interface RegisterRequest {
    email: string;
    password: string;
}

export interface LoginResponse {
    tokenType: string;
    accessToken: string;
    expiresIn: number;
    refreshToken: string;
}

export interface TransactionRequest {
    sourceAccountId: string;
    targetAccountId: string;
    amount: number;
    currency: string;
}

export interface TransactionResponse {
    id: string;
    status: string;
}

export interface ValidationError {
    [key: string]: string[];
}

export interface AccountDto {
    id: string;
    currency: string;
    ownerId: string;
}

export class ApiValidationError extends Error {
    validationErrors: ValidationError;

    constructor(message: string, errors: ValidationError) {
        super(message);
        this.name = "ApiValidationError";
        this.validationErrors = errors;
    }
}

export const loginUser = async (creds: LoginRequest): Promise<LoginResponse> => {
    const response = await fetch(`${API_BASE_URL}/api/auth/login?useCookies=false`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(creds)
    });

    if (!response.ok) {
        throw new Error('Login failed. Check credentials.');
    }
    return response.json();
};

export const registerUser = async (creds: RegisterRequest) => {
    const response = await fetch(`${API_BASE_URL}/api/auth/register`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(creds)
    });

    if (!response.ok) {
        const contentType = response.headers.get("content-type");
        if (contentType && contentType.indexOf("application/json") !== -1) {
            const err = await response.json();
            let errorMessage = 'Registration failed';
            if (err.errors) {
                errorMessage = Object.values(err.errors).flat().join(', ');
            }
            throw new Error(errorMessage);
        } else {
            const text = await response.text();
            console.error("API Error:", text);
            throw new Error("Server Error: Check API console logs.");
        }
    }
};

export const submitTransaction = async (data: TransactionRequest, token: string): Promise<TransactionResponse> => {
    const response = await fetch(`${API_BASE_URL}/api/transactions`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${token}`
        },
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        if (response.status === 401) throw new Error("Unauthorized: Session expired");

        if (response.status === 400) {
            const errorData = await response.json();

            if (errorData.errors) {
                throw new ApiValidationError("Validation Failed", errorData.errors);
            }
        }
        throw new Error('Transaction submission failed');
    }

    return response.json();
};

export const getMyAccount = async (token: string, signal?: AbortSignal): Promise<AccountDto | null> => {
    const response = await fetch(`${API_BASE_URL}/api/accounts/my-account`, {
        headers: { 'Authorization': `Bearer ${token}` },
        signal
    });

    if (response.status === 404) return null;
    if (!response.ok) throw new Error('Failed to fetch account');
    return response.json();
};

export const provisionAccount = async (token: string, signal?: AbortSignal): Promise<AccountDto> => {
    const response = await fetch(`${API_BASE_URL}/api/accounts/provision`, {
        method: 'POST',
        headers: { 'Authorization': `Bearer ${token}` },
        signal
    });

    if (!response.ok) throw new Error('Failed to provision account');
    return response.json();
};