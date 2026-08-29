export interface CardDto {
  rank: string;
  suit: string;
}

export interface SeatDto {
  index: number;
  playerId: string | null;
  stack: number;
  pendingRebuyChips: number;
  isAllIn: boolean;
  isFolded: boolean;
  holeCards: CardDto[] | null;
  revealedHandName: string | null;
}

export interface PotDto {
  amount: number;
  winnerPlayerIds: string[];
  eligiblePlayerIds: string[];
}

export interface HandDto {
  street: "Preflop" | "Flop" | "Turn" | "River" | "Showdown";
  board: CardDto[];
  currentActorPlayerId: string | null;
  result: PotDto[] | null;
}

export interface EquityDto {
  playerId: string;
  winPercent: number;
  tiePercent: number;
}

export interface TableStateDto {
  tableId: string;
  name: string;
  status: "WaitingForPlayers" | "Playing";
  minBuyIn: number;
  maxBuyIn: number;
  seats: SeatDto[];
  spectators: string[];
  waitlistCount: number;
  hand: HandDto | null;
  equity: EquityDto[] | null;
}

export interface ChatMessageDto {
  userId: string;
  message: string;
  isSpectator: boolean;
  sentAtUtc: string;
}

export const BettingActionType = {
  Fold: 0,
  Check: 1,
  Call: 2,
  Bet: 3,
  Raise: 4,
} as const;
