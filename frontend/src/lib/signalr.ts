import * as signalR from "@microsoft/signalr";
import { API_URL } from "./config";

export function createHubConnection(hubPath: string): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${API_URL}${hubPath}`, { withCredentials: true })
    .withAutomaticReconnect()
    .build();
}
