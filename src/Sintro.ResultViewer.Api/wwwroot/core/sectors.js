// =============================================================================
// Hit-sector geometry. Pure — returns SVG path data, draws nothing.
//
// The device reports hitSector 1-8, and the meaning was derived from the stored X/Y
// coordinates: the mean atan2(y, x) per sector comes out at 90°, 43°, -2°, -45°, -89°,
// -136°, 180° and 137°. So sector 1 is twelve o'clock and the numbers run CLOCKWISE in
// exact 45° steps. Sector 0 is a centre hit; null means the device reported none.
// =============================================================================

export const SECTOR_COUNT = 8;
export const SECTOR_SPAN_DEGREES = 360 / SECTOR_COUNT;
export const CENTRE_SECTOR = 0;

/** Mathematical angle (0° = right, counter-clockwise) at the middle of a sector. */
export const sectorCentreAngle = (sector) => 90 - (sector - 1) * SECTOR_SPAN_DEGREES;

/**
 * A point on the circle. SVG's y axis points down, so the mathematical angle is mirrored —
 * without that, the whole dial would be flipped vertically and sector 2 would read as 4:30.
 */
export const pointOnCircle = (angleDegrees, { cx, cy, r }) => {
    const radians = (angleDegrees * Math.PI) / 180;
    return {
        x: cx + r * Math.cos(radians),
        y: cy - r * Math.sin(radians),
    };
};

const round = (value) => Math.round(value * 1000) / 1000;

/** A full pie wedge. Not drawn by the viewer; kept as the reference shape the ring is cut from. */
export const wedgePath = (sector, geometry) => {
    const centre = sectorCentreAngle(sector);
    const from = pointOnCircle(centre + SECTOR_SPAN_DEGREES / 2, geometry);
    const to = pointOnCircle(centre - SECTOR_SPAN_DEGREES / 2, geometry);

    // sweep-flag 1: decreasing mathematical angle is clockwise once y is flipped.
    return `M ${round(geometry.cx)} ${round(geometry.cy)} `
         + `L ${round(from.x)} ${round(from.y)} `
         + `A ${round(geometry.r)} ${round(geometry.r)} 0 0 1 ${round(to.x)} ${round(to.y)} Z`;
};

/**
 * SVG path for one wedge of a RING rather than a full pie, leaving the middle free for the
 * shot's ring value. The dial is context around the number, never a replacement for it.
 */
export const ringWedgePath = (sector, geometry) => {
    const { cx, cy, r } = geometry;
    const innerR = geometry.innerR ?? r * 0.62;
    const centre = sectorCentreAngle(sector);
    const half = SECTOR_SPAN_DEGREES / 2;

    const outerFrom = pointOnCircle(centre + half, { cx, cy, r });
    const outerTo = pointOnCircle(centre - half, { cx, cy, r });
    const innerTo = pointOnCircle(centre - half, { cx, cy, r: innerR });
    const innerFrom = pointOnCircle(centre + half, { cx, cy, r: innerR });

    return `M ${round(outerFrom.x)} ${round(outerFrom.y)} `
         + `A ${round(r)} ${round(r)} 0 0 1 ${round(outerTo.x)} ${round(outerTo.y)} `
         + `L ${round(innerTo.x)} ${round(innerTo.y)} `
         + `A ${round(innerR)} ${round(innerR)} 0 0 0 ${round(innerFrom.x)} ${round(innerFrom.y)} Z`;
};

/** Midpoint of a wedge — the visual centre of gravity, used to verify orientation. */
export const wedgeMidpoint = (sector, geometry) =>
    pointOnCircle(sectorCentreAngle(sector), { ...geometry, r: geometry.r / 2 });

// A thin ring: the hole has to hold a three-digit value, because the 100er valuation scores
// 0-100. Anything fatter and "100" no longer fits inside it.
export const DEFAULT_DIAL_GEOMETRY = { cx: 10, cy: 10, r: 9.5, innerR: 6.8 };

/**
 * Describes the dial for one shot: which wedge is filled, and whether the centre is hit.
 *
 * A miss still gets a dial — the sector says where it went, which is the whole point of
 * showing direction rather than only the ring value.
 */
export const shotDial = (shot, geometry = DEFAULT_DIAL_GEOMETRY) => {
    const sector = shot?.hitSector ?? null;
    const isCentre = sector === CENTRE_SECTOR;

    return {
        geometry,
        isCentre,
        filledSector: isCentre || sector === null ? null : sector,
        wedges: Array.from({ length: SECTOR_COUNT }, (_, index) => {
            const number = index + 1;
            return {
                sector: number,
                ringPath: ringWedgePath(number, geometry),
                filled: number === sector,
            };
        }),
    };
};
