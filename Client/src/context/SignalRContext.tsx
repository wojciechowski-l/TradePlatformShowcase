import React, { createContext, useContext, useEffect, useState } from 'react';
import * as signalR from '@microsoft/signalr';

interface SignalRContextType {
    connection: signalR.HubConnection | null;
}

const SignalRContext = createContext<SignalRContextType>({ connection: null });

export const useSignalR = () => useContext(SignalRContext);

export const SignalRProvider = ({ children, token }: { children: React.ReactNode, token: string | null }) => {
    const [connection, setConnection] = useState<signalR.HubConnection | null>(null);

    const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:5046';

    useEffect(() => {
        if (!token) return;

        const newConnection = new signalR.HubConnectionBuilder()
            .withUrl(`${API_URL}/hubs/trade`, {
                accessTokenFactory: () => token 
            })
            .withAutomaticReconnect()
            .build();

        newConnection.start()
            .then(() => {
                console.log("SignalR Connected");
                setConnection(newConnection);
            })
            .catch(err => console.error("SignalR Connection Error: ", err));

        return () => {
            newConnection.stop();
        };
    }, [token, API_URL]);

    return (
        <SignalRContext.Provider value={{ connection }}>
            {children}
        </SignalRContext.Provider>
    );
};