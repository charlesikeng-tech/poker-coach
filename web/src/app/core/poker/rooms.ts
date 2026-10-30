/**
 * Poker rooms, as the API names them (PokerCoach.Domain.Poker.PokerRoom, camelCase). The product talks about
 * rooms in general; a room's name only appears where the text is about that room (its account, where it
 * saves its files). Add a room here when its import ships.
 */
export const SUPPORTED_ROOMS = ['winamax', 'betclic', 'pmu'] as const;

export type SupportedRoom = (typeof SUPPORTED_ROOMS)[number];

/** Brand names are the same in every language. */
const ROOM_NAMES: Readonly<Record<string, string>> = {
  winamax: 'Winamax',
  pokerStars: 'PokerStars',
  betclic: 'Betclic',
  pmu: 'PMU Poker',
};

/** The room's display name; an unknown value is shown as the API sent it. */
export function roomName(room: string): string {
  return ROOM_NAMES[room] ?? room;
}

/** Maps rooms to their logo file extension in /rooms/. */
const ROOM_LOGO_EXT: Readonly<Record<string, string>> = {
  pmu: 'png',
};

/** Path to the room's logo in /rooms/, or null if unknown. */
export function roomLogo(room: string): string {
  const ext = ROOM_LOGO_EXT[room] ?? 'svg';
  return `/rooms/${room}.${ext}`;
}
