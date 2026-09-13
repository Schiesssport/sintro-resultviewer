// Sector 1 is twelve o'clock and the numbers run clockwise in 45° steps (mean atan2 of the stored X/Y).

export const SECTOR_COUNT = 8;
const SECTOR_SPAN_DEGREES = 360 / SECTOR_COUNT;
const CENTRE_SECTOR = 0;

// Mathematical angle (0° = right, counter-clockwise) at the middle of a sector.
export const sectorCentreAngle = (sector) => 90 - (sector - 1) * SECTOR_SPAN_DEGREES;

// SVG's y axis points down, so the mathematical angle is mirrored.
export const pointOnCircle = (angleDegrees, { cx, cy, r }) => {
    const radians = (angleDegrees * Math.PI) / 180;
    return {
        x: cx + r * Math.cos(radians),
        y: cy - r * Math.sin(radians),
    };
};

const round = (value) => Math.round(value * 1000) / 1000;

// A ring wedge rather than a pie slice, leaving the middle free for the shot's ring value.
export const ringWedgePath = (sector, { cx, cy, r, innerR }) => {
    const centre = sectorCentreAngle(sector);
    const half = SECTOR_SPAN_DEGREES / 2;

    const outerFrom = pointOnCircle(centre + half, { cx, cy, r });
    const outerTo = pointOnCircle(centre - half, { cx, cy, r });
    const innerTo = pointOnCircle(centre - half, { cx, cy, r: innerR });
    const innerFrom = pointOnCircle(centre + half, { cx, cy, r: innerR });

    // sweep-flag 1: decreasing mathematical angle is clockwise once y is flipped.
    return `M ${round(outerFrom.x)} ${round(outerFrom.y)} `
         + `A ${round(r)} ${round(r)} 0 0 1 ${round(outerTo.x)} ${round(outerTo.y)} `
         + `L ${round(innerTo.x)} ${round(innerTo.y)} `
         + `A ${round(innerR)} ${round(innerR)} 0 0 0 ${round(innerFrom.x)} ${round(innerFrom.y)} Z`;
};

// The hole must hold a three-digit value: the 100er valuation scores 0-100.
export const DEFAULT_DIAL_GEOMETRY = { cx: 10, cy: 10, r: 9.5, innerR: 6.8 };

// A miss still gets a dial: the sector says where it went.
export const shotDial = (shot, geometry = DEFAULT_DIAL_GEOMETRY) => {
    const sector = shot?.hitSector ?? null;

    return {
        geometry,
        isCentre: sector === CENTRE_SECTOR,
        wedges: Array.from({ length: SECTOR_COUNT }, (_, index) => ({
            sector: index + 1,
            ringPath: ringWedgePath(index + 1, geometry),
            filled: index + 1 === sector,
        })),
    };
};
